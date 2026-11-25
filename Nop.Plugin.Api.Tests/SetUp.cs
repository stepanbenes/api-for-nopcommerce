using Nop.Core.Infrastructure.Mapper;
using Nop.Plugin.Api.MappingExtensions;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests;

[SetUpFixture]
public class SetUp
{
    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        MapperConfiguration.Init(new IOrderedMapperProfile[] { new ApiMapperConfiguration() }.OrderBy(profile => profile.Order));
    }
}
