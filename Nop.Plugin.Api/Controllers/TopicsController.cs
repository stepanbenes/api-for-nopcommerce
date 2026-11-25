using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Topics;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTOs.Topics;
using Nop.Plugin.Api.Factories;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.TopicsParameters;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Services.Topics;
using System.Net;

namespace Nop.Plugin.Api.Controllers
{
    [AuthorizePermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public class TopicsController : BaseApiController
    {
        private readonly ITopicService _topicService;
        private readonly IStoreContext _storeContext;
        private readonly IDTOHelper _dtoHelper;
        private readonly IFactory<Topic> _factory;

        public TopicsController(
            IJsonFieldsSerializer jsonFieldsSerializer,
            IAclService aclService,
            ITopicService topicService,
            ICustomerService customerService,
            IStoreMappingService storeMappingService,
            IStoreService storeService,
            IDiscountService discountService,
            ICustomerActivityService customerActivityService,
            ILocalizationService localizationService,
            IStoreContext storeContext,
            IDTOHelper dtoHelper,
            IPictureService pictureService,
            IFactory<Topic> factory

            ) : base(jsonFieldsSerializer, aclService, customerService, storeMappingService,
                   storeService, discountService, customerActivityService, localizationService, pictureService)
        {
            _topicService = topicService;
            _storeContext = storeContext;
            _dtoHelper = dtoHelper;
            _factory = factory;
        }

        /// <summary>
        ///     Receive a list of all Topics
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/topics", Name = "GetTopics")]
        [ProducesResponseType(typeof(TopicsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [GetRequestsErrorInterceptorActionFilter]
        [UnpagedCollection]
        public async Task<IActionResult> GetTopics([FromQuery] TopicsParametersModel parameters)
        {
            if (parameters.Limit < 1 || parameters.Limit > Constants.Configurations.MaxLimit)
                return Error(HttpStatusCode.BadRequest, "limit", "invalid limit");
            if (parameters.Page < 1)
                return Error(HttpStatusCode.BadRequest, "page", "invalid page");

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;

            var topics = await _topicService.GetAllTopicsAsync(storeId);

            var matchingTopics = topics.Where(topic =>
                (parameters.Ids == null || parameters.Ids.Count == 0 || parameters.Ids.Contains(topic.Id)) &&
                topic.Id > parameters.SinceId);
            var limit = GetCollectionLimit(parameters.Limit);
            var offset = (long)(parameters.Page - 1) * limit;
            IList<TopicDto> topicsAsDtos = offset >= topics.Count
                ? new List<TopicDto>()
                : matchingTopics.Skip((int)offset).Take(limit).Select(_dtoHelper.PrepareTopicDTO).ToList();

            var topicsRootObject = new TopicsRootObject
            {
                Topics = topicsAsDtos
            };

            var json = JsonFieldsSerializer.Serialize(topicsRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Retrieve topic by specified id
        /// </summary>
        /// ///
        /// <param name="id">Id of the topic</param>
        /// <param name="fields">Fields from the topic you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/topics/{id}", Name = "GetTopicById")]
        [ProducesResponseType(typeof(TopicsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetTopicById([FromRoute] int id, [FromQuery] string fields = "")
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var topic = await _topicService.GetTopicByIdAsync(id);

            if (topic == null)
            {
                return Error(HttpStatusCode.NotFound, "topic", "not found");
            }

            var topicsRootObject = new TopicsRootObject();

            var topicDto = _dtoHelper.PrepareTopicDTO(topic);
            topicsRootObject.Topics.Add(topicDto);

            var json = JsonFieldsSerializer.Serialize(topicsRootObject, fields);

            return new RawJsonActionResult(json);
        }

        [HttpPost]
        [Route("/api/topics", Name = "CreateTopic")]
        [ProducesResponseType(typeof(TopicsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        public async Task<IActionResult> CreateTopic(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<TopicDto>))]
            Delta<TopicDto> topicDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            var topicDtoInput = topicDelta.Dto;
            if (topicDtoInput.IncludeInTopMenu.HasValue || topicDtoInput.IncludeInFooterColumn1.HasValue ||
                topicDtoInput.IncludeInFooterColumn2.HasValue || topicDtoInput.IncludeInFooterColumn3.HasValue)
            {
                return Error(HttpStatusCode.BadRequest, "menu", "Manage navigation in nopCommerce under Content management > Menus.");
            }

            var newTopic = await _factory.InitializeAsync();
            newTopic.Title = topicDelta.Dto.Title;
            newTopic.Body = topicDelta.Dto.Body;
            newTopic.SystemName = topicDelta.Dto.SystemName;

            await _topicService.InsertTopicAsync(newTopic);

            var topicsRootObject = new TopicsRootObject();

            var topicDto = _dtoHelper.PrepareTopicDTO(newTopic);

            topicsRootObject.Topics.Add(topicDto);

            var json = JsonFieldsSerializer.Serialize(topicsRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }

        [HttpPut]
        [Route("/api/topics/{id}", Name = "UpdateTopic")]
        [ProducesResponseType(typeof(TopicsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        public async Task<IActionResult> UpdateTopic(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<TopicDto>))]
            Delta<TopicDto> topicDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            var topicDtoInput = topicDelta.Dto;
            if (topicDtoInput.IncludeInTopMenu.HasValue || topicDtoInput.IncludeInFooterColumn1.HasValue ||
                topicDtoInput.IncludeInFooterColumn2.HasValue || topicDtoInput.IncludeInFooterColumn3.HasValue)
            {
                return Error(HttpStatusCode.BadRequest, "menu", "Manage navigation in nopCommerce under Content management > Menus.");
            }

            var currentTopic = await _topicService.GetTopicByIdAsync(topicDelta.Dto.Id);

            if (currentTopic == null)
            {
                return Error(HttpStatusCode.NotFound, "topic", "not found");
            }

            topicDelta.Merge(currentTopic);

            await _topicService.UpdateTopicAsync(currentTopic);

            var topicsRootObject = new TopicsRootObject();

            var topicDto = _dtoHelper.PrepareTopicDTO(currentTopic);

            topicsRootObject.Topics.Add(topicDto);

            var json = JsonFieldsSerializer.Serialize(topicsRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }

        [HttpDelete]
        [Route("/api/topics/{id}", Name = "DeleteTopic")]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> DeleteTopic([FromRoute] int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var topicToDelete = await _topicService.GetTopicByIdAsync(id);

            if (topicToDelete == null)
            {
                return Error(HttpStatusCode.NotFound, "topic", "not found");
            }

            await _topicService.DeleteTopicAsync(topicToDelete);

            //activity log
            await CustomerActivityService.InsertActivityAsync("DeleteTopic", await LocalizationService.GetResourceAsync("ActivityLog.DeleteTopic"), topicToDelete);

            return Json(new { status = "ok" });
        }
    }
}
