using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Menus;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.DTOs.Menus;
using Nop.Services.Catalog;
using Nop.Services.Menus;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Services.Topics;

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
    ICategoryService categoryService,
    ITopicService topicService,
    IAclService aclService,
    IStoreMappingService storeMappingService) : ControllerBase
{
    // The upstream service has no atomic find-or-insert operation.
    private static readonly SemaphoreSlim MenuWrites = new(1);

    [HttpGet(Name = "GetMenus")]
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
    [ProducesResponseType(typeof(MenuItemsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MenuItemsResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMenuItem(int menuId, AddMenuItemRequest request)
    {
        await MenuWrites.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (!await IsInScope(await menuService.GetMenuByIdAsync(menuId)))
                return NotFound();

            var type = request.LinkType == "category" ? MenuItemType.Category : MenuItemType.TopicPage;
            string title;
            if (type == MenuItemType.Category)
            {
                var category = await categoryService.GetCategoryByIdAsync(request.EntityId);
                if (category is null || category.Deleted || !await IsInScope(category))
                    return NotFound();

                title = category.Name;
            }
            else
            {
                var topic = await topicService.GetTopicByIdAsync(request.EntityId);
                if (!await IsInScope(topic))
                    return NotFound();

                title = topic.Title;
            }

            var items = await menuService.GetAllMenuItemsAsync(menuId: menuId, showHidden: true);
            var existing = items.FirstOrDefault(item => item.MenuItemType == type && item.EntityId == request.EntityId);
            if (existing is not null)
                return await IsInScope(existing) ? Ok(ItemResponse(existing)) : Conflict();

            var lastOrder = items.Where(item => item.ParentId == 0).Select(item => item.DisplayOrder)
                .DefaultIfEmpty(0).Max();
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
        }
        finally
        {
            MenuWrites.Release();
        }
    }

    [HttpDelete("{menuId}/items/{itemId}", Name = "RemoveMenuItem")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMenuItem(int menuId, int itemId)
    {
        await MenuWrites.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (!await IsInScope(await menuService.GetMenuByIdAsync(menuId)))
                return NotFound();

            var item = await menuService.GetMenuItemByIdAsync(itemId);
            if (item is null || item.MenuId != menuId || item.EntityId is null ||
                item.MenuItemType is not (MenuItemType.Category or MenuItemType.TopicPage) || !await IsInScope(item))
                return NotFound();

            // Upstream deletion cascades. Refuse to remove an item that has children.
            var children = await menuService.GetAllMenuItemsAsync(parentMenuItemId: item.Id, showHidden: true, pageSize: 1);
            if (children.Count != 0)
                return Conflict(new { error = "This menu item has child items." });

            await menuService.DeleteMenuItemAsync(item);
            return NoContent();
        }
        finally
        {
            MenuWrites.Release();
        }
    }

    private async Task<bool> IsInScope<TEntity>(TEntity entity)
        where TEntity : BaseEntity, IAclSupported, IStoreMappingSupported =>
        entity is not null && await storeMappingService.AuthorizeAsync(entity) && await aclService.AuthorizeAsync(entity);

    private static MenuItemsResponse ItemResponse(MenuItem item) => new(new[] { ToDto(item) });

    private static MenuItemDto ToDto(MenuItem item) => new(item.Id, item.MenuId, item.ParentId, item.MenuItemType,
        item.EntityId, item.Title, item.Url, item.Published, item.DisplayOrder);
}
