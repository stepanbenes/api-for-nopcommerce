using Newtonsoft.Json;
using Nop.Plugin.Api.DTO;

namespace Nop.Plugin.Api.DTOs.Topics
{
    public class TopicsRootObject : ISerializableObject
    {
        public TopicsRootObject()
        {
            Topics = new List<TopicDto>();
        }

        [JsonProperty("topics")]
        public IList<TopicDto> Topics { get; set; }

        public string GetPrimaryPropertyName()
        {
            return "topics";
        }

        public Type GetPrimaryPropertyType()
        {
            return typeof(TopicDto);
        }
    }
}
