using Nop.Plugin.Api.Converters;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests.ConvertersTests.ApiTypeConverter
{
    [TestFixture]
    public class ApiTypeConverterTests_ToBoolean
    {
        private IApiTypeConverter _apiTypeConverter;

        [SetUp]
        public void SetUp()
        {
            _apiTypeConverter = new Converters.ApiTypeConverter();
        }

        [Test]
        [TestCase("invalid status")]
        [TestCase("publicshed")]
        [TestCase("un-published")]
        [TestCase("322345")]
        [TestCase("%^)@*%&*@_!+=")]
        [TestCase("1")]
        public void WhenInvalidValuePassed_ShouldReturnNull(string invalidValue)
        {
            //Arange

            //Act
            bool? result = _apiTypeConverter.ToBoolean(invalidValue);

            //Assert
            Assert.IsNull(result);
        }

        [Test]
        [TestCase("")]
        [TestCase(null)]
        public void WhenNullOrEmptyStringPassed_ShouldReturnNull(string nullOrEmpty)
        {
            //Arange

            //Act
            bool? result = _apiTypeConverter.ToBoolean(nullOrEmpty);

            //Assert
            Assert.IsNull(result);
        }

        [Test]
        [TestCase("true")]
        [TestCase("True")]
        [TestCase("TruE")]
        public void WhenValidTrueValuePassed_ShouldReturnTrue(string trueValue)
        {
            //Arange

            //Act
            bool? result = _apiTypeConverter.ToBoolean(trueValue);

            //Assert
            Assert.IsTrue(result.Value);
        }

        [Test]
        [TestCase("false")]
        [TestCase("False")]
        [TestCase("FalsE")]
        public void WhenValidFalseValuePassed_ShouldReturnFalse(string falseValue)
        {
            //Arange

            //Act
            bool? result = _apiTypeConverter.ToBoolean(falseValue);

            //Assert
            Assert.IsFalse(result.Value);
        }
    }
}