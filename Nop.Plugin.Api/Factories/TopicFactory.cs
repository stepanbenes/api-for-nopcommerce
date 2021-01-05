using Nop.Core.Domain.Topics;

namespace Nop.Plugin.Api.Factories
{
    public class TopicFactory : IFactory<Topic>
    {
        public Topic Initialize()
        {
            var topic = new Topic();
            return topic;
        }
    }
}
