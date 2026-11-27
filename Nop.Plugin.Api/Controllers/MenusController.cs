using LinqToDB.Async;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Menus;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Topics;
using Nop.Data;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.DTOs.Menus;
using Nop.Plugin.Api.Services;
using Nop.Services.Menus;
using Nop.Services.Security;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "v1")]
[Authorize(Policy = JwtBearerDefaults.AuthenticationScheme, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[AuthorizePermission(StandardPermission.ContentManagement.MENU_CREATE_EDIT_DELETE)]
[Route("api/menus")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class MenusController(
    IMenuService menuService,
    IRepository<Menu> menuRepository,
    IRepository<Category> categoryRepository,
    IRepository<Topic> topicRepository,
    RelationshipWrites<MenuItem> writes,
    RelationshipWriteScope scope,
    IAclService aclService,
    IStoreMappingService storeMappingService) : ControllerBase
{
    [HttpGet(Name = "GetMenus")]
    [ApiAudience(ApiAudience.BackOffice)]
    [ProducesResponseType(typeof(MenusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMenus()
    {
        var menus = await menuService.GetAllMenusAsync(showHidden: true);
        var result = new List<MenuDto>();
        foreach (var menu in menus)
        {
            if (await IsInScope(menu))
                result.Add(new MenuDto(menu.Id, menu.Name, menu.MenuType, menu.Published,
                    menu.DisplayOrder, menu.DisplayAllCategories));
        }

        return Ok(new MenusResponse(result));
    }

    [HttpGet("{menuId}/items", Name = "GetMenuItems")]
    [ApiAudience(ApiAudience.BackOffice)]
    [ProducesResponseType(typeof(MenuItemsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMenuItems(int menuId)
    {
        if (!await IsInScope(await menuService.GetMenuByIdAsync(menuId)))
            return NotFound();

        var items = await menuService.GetAllMenuItemsAsync(menuId: menuId, showHidden: true);
        var result = new List<MenuItemDto>();
        foreach (var item in items)
        {
            if (await IsInScope(item))
                result.Add(ToDto(item));
        }

        return Ok(new MenuItemsResponse(result));
    }

    [HttpPost("{menuId}/items", Name = "AddMenuItem")]
    [ApiAudience(ApiAudience.BackOffice)]
    [ProducesResponseType(typeof(MenuItemsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MenuItemsResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AddMenuItem(int menuId, AddMenuItemRequest request)
    {
        return await writes.ExecuteAsync(async _ =>
        {
            if (!await scope.IsInScopeAsync(await menuRepository.GetByIdAsync(menuId, includeDeleted: false)))
                return NotFound();

            var type = request.LinkType == "category" ? MenuItemType.Category : MenuItemType.TopicPage;
            string title;
            if (type == MenuItemType.Category)
            {
                var category = await categoryRepository.GetByIdAsync(request.EntityId, includeDeleted: false);
                if (!await scope.IsInScopeAsync(category))
                    return NotFound();

                title = category.Name;
            }
            else
            {
                var topic = await topicRepository.GetByIdAsync(request.EntityId);
                if (!await scope.IsInScopeAsync(topic))
                    return NotFound();

                title = topic.Title;
            }

            var items = writes.Table.Where(item => item.MenuId == menuId);
            var matching = await items.Where(item => item.MenuItemTypeId == (int)type && item.EntityId == request.EntityId)
                .Take(2).ToListAsync(HttpContext.RequestAborted);
            if (matching.Count != 0)
            {
                var existing = matching[0];
                if (matching.Count > 1 || !await scope.IsInScopeAsync(existing) ||
                    (request.DisplayOrder is int order && existing.DisplayOrder != order))
                    return Conflict();

                return Ok(ItemResponse(existing));
            }

            var lastOrder = !request.DisplayOrder.HasValue
                ? await items.Where(item => item.ParentId == 0).Select(item => (int?)item.DisplayOrder)
                    .MaxAsync(HttpContext.RequestAborted) ?? 0
                : 0;
            if (!request.DisplayOrder.HasValue && lastOrder == int.MaxValue)
                return Conflict(new { error = "Supply a display_order because this menu has reached the maximum display order." });

            var link = new MenuItem
            {
                MenuId = menuId,
                MenuItemType = type,
                EntityId = request.EntityId,
                Title = title,
                Published = true,
                Template = MenuItemTemplate.Simple,
                DisplayOrder = request.DisplayOrder ?? lastOrder + 1
            };
            await menuService.InsertMenuItemAsync(link);
            return CreatedAtRoute("GetMenuItems", new { menuId }, ItemResponse(link));
        }, HttpContext.RequestAborted);
    }

    [HttpPut("{menuId}/items/{itemId}", Name = "UpdateMenuItemOrder")]
    [ApiAudience(ApiAudience.BackOffice)]
    [ProducesResponseType(typeof(MenuItemsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMenuItemOrder(int menuId, int itemId, UpdateMenuItemOrderRequest request)
    {
        return await writes.ExecuteAsync(async _ =>
        {
            if (!await scope.IsInScopeAsync(await menuRepository.GetByIdAsync(menuId, includeDeleted: false)))
                return NotFound();

            var item = await writes.GetByIdAsync(itemId);
            if (!await IsEditableLink(item, menuId))
                return NotFound();

            if (item.DisplayOrder == request.DisplayOrder.Value)
                return Ok(ItemResponse(item));

            if (item.DisplayOrder != request.ExpectedDisplayOrder.Value)
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "The menu item's display order changed.");

            item.DisplayOrder = request.DisplayOrder.Value;
            await menuService.UpdateMenuItemAsync(item);
            return Ok(ItemResponse(item));
        }, HttpContext.RequestAborted);
    }

    [HttpDelete("{menuId}/items/{itemId}", Name = "RemoveMenuItem")]
    [ApiAudience(ApiAudience.BackOffice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RemoveMenuItem(int menuId, int itemId)
    {
        return await writes.ExecuteAsync(async _ =>
        {
            if (!await scope.IsInScopeAsync(await menuRepository.GetByIdAsync(menuId, includeDeleted: false)))
                return NotFound();

            var item = await writes.GetByIdAsync(itemId);
            if (!await IsEditableLink(item, menuId))
                return NotFound();

            // Upstream deletion cascades. Refuse to remove an item that has children.
            if (await writes.Table.AnyAsync(child => child.ParentId == item.Id, HttpContext.RequestAborted))
                return Conflict(new { error = "This menu item has child items." });

            await menuService.DeleteMenuItemAsync(item);
            return NoContent();
        }, HttpContext.RequestAborted);
    }

    private async Task<bool> IsEditableLink(MenuItem item, int menuId)
    {
        if (item is null || item.MenuId != menuId || item.EntityId is not int entityId || !await scope.IsInScopeAsync(item))
            return false;

        switch (item.MenuItemType)
        {
            case MenuItemType.Category:
                var category = await categoryRepository.GetByIdAsync(entityId, includeDeleted: false);
                return await scope.IsInScopeAsync(category);
            case MenuItemType.TopicPage:
                return await scope.IsInScopeAsync(await topicRepository.GetByIdAsync(entityId));
            default:
                return false;
        }
    }

    private async Task<bool> IsInScope<TEntity>(TEntity entity)
        where TEntity : BaseEntity, IAclSupported, IStoreMappingSupported =>
        entity is not null && await storeMappingService.AuthorizeAsync(entity) && await aclService.AuthorizeAsync(entity);

    private static MenuItemsResponse ItemResponse(MenuItem item) => new(new[] { ToDto(item) });

    private static MenuItemDto ToDto(MenuItem item) => new(item.Id, item.MenuId, item.ParentId, item.MenuItemType,
        item.EntityId, item.Title, item.Url, item.Published, item.DisplayOrder);
}
