using System;
using System.Collections.Generic;
using Nop.Plugin.Api.Converters;
using Nop.Plugin.Api.Tests.ConvertersTests.ObjectConverter.DummyObjects;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ConvertersTests.ObjectConverter
{
    [TestFixture]
    public class ObjectConverterTests_ToObject
    {
        [Test]
        public void WhenCollectionIsNull_ShouldNotCallAnyOfTheApiTypeConverterMethods()
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> nullCollection = null;

            //Act
            objectConverter.ToObject<SomeTestingObject>(nullCollection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToInt(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToIntNullable(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToUtcDateTimeNullable(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToListOfInts(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToBoolean(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void WhenCollectionIsNull_ShouldReturnInstanceOfAnObjectOfTheSpecifiedType()
        {
            //Arange
            IApiTypeConverter apiTypeConverterStub = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterStub);

            ICollection<KeyValuePair<string, string>> nullCollection = null;

            //Act
            SomeTestingObject someTestingObject = objectConverter.ToObject<SomeTestingObject>(nullCollection);

            //Assert
            Assert.IsNotNull(someTestingObject);
            Assert.IsInstanceOf(typeof(SomeTestingObject), someTestingObject);
        }

        [Test]
        public void WhenCollectionIsNull_ShouldReturnInstanceOfAnObjectWithUnsetProperties()
        {
            //Arange
            IApiTypeConverter apiTypeConverterStub = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterStub);

            ICollection<KeyValuePair<string, string>> nullCollection = null;

            //Act
            SomeTestingObject someTestingObject = objectConverter.ToObject<SomeTestingObject>(nullCollection);

            //Assert
            Assert.AreEqual(0, someTestingObject.IntProperty);
            Assert.AreEqual(null, someTestingObject.StringProperty);
            Assert.AreEqual(null, someTestingObject.DateTimeNullableProperty);
            Assert.AreEqual(null, someTestingObject.BooleanNullableStatusProperty);
        }

        [Test]
        public void WhenCollectionIsEmpty_ShouldReturnInstanceOfAnObjectWithUnsetProperties()
        {
            //Arange
            IApiTypeConverter apiTypeConverterStub = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterStub);

            ICollection<KeyValuePair<string, string>> emptyCollection = new List<KeyValuePair<string, string>>();

            //Act
            SomeTestingObject someTestingObject = objectConverter.ToObject<SomeTestingObject>(emptyCollection);

            //Assert
            Assert.AreEqual(0, someTestingObject.IntProperty);
            Assert.AreEqual(null, someTestingObject.StringProperty);
            Assert.AreEqual(null, someTestingObject.DateTimeNullableProperty);
            Assert.AreEqual(null, someTestingObject.BooleanNullableStatusProperty);
        }
        [Test]
        public void WhenCollectionIsEmpty_ShoulNotCallAnyOfTheApiTypeConverterMethods()
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> emptyCollection = new List<KeyValuePair<string, string>>();

            //Act
            objectConverter.ToObject<SomeTestingObject>(emptyCollection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToInt(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToIntNullable(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToUtcDateTimeNullable(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToListOfInts(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToBoolean(It.IsAny<string>()), Times.Never());
        }

        [Test]
        [TestCase("IntProperty")]
        [TestCase("Int_Property")]
        [TestCase("int_property")]
        [TestCase("intproperty")]
        [TestCase("inTprOperTy")]
        public void WhenCollectionContainsValidIntProperty_ShouldCallTheToIntMethod(string intPropertyName)
        {
            //Arange
            int expectedInt = 5;
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();
            Mock.Get(apiTypeConverterMock).Setup(x => x.ToInt(It.IsAny<string>())).Returns(expectedInt).Verifiable();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(intPropertyName, expectedInt.ToString())
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify();
        }
        
        [Test]
        [TestCase("invalid int property name")]
        [TestCase("34534535345345345345345345345345345345345")]
        public void WhenCollectionContainsInvalidIntProperty_ShouldNotCallTheToIntMethod(string invalidIntPropertyName)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(invalidIntPropertyName, "5")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToInt(It.IsAny<string>()), Times.Never());
        }

        [Test]
        [TestCase("StringProperty")]
        [TestCase("String_Property")]
        [TestCase("string_property")]
        [TestCase("stringproperty")]
        [TestCase("strInGprOperTy")]
        public void WhenCollectionContainsValidStringProperty_ShouldSetTheObjectStringPropertyValueToTheCollectionStringPropertyValue(string stringPropertyName)
        {
            //Arange
            IApiTypeConverter apiTypeConverterStub = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterStub);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(stringPropertyName, "some value")
            };

            //Act
            SomeTestingObject someTestingObject = objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Assert.AreEqual("some value", someTestingObject.StringProperty);
        }

        [Test]
        [TestCase("invalid string property name")]
        public void WhenCollectionContainsInvalidStringProperty_ShouldReturnTheObjectWithItsStringPropertySetToTheDefaultValue(string invalidStringPropertyName)
        {
            //Arange
            IApiTypeConverter apiTypeConverterStub = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterStub);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(invalidStringPropertyName, "some value")
            };

            //Act
            SomeTestingObject someTestingObject = objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Assert.IsNull(someTestingObject.StringProperty);
        }

        [Test]
        [TestCase("invalid string property name")]
        [TestCase("StringProperty")]
        [TestCase("String_Property")]
        [TestCase("string_property")]
        [TestCase("stringproperty")]
        [TestCase("strInGprOperTy")]
        public void WhenCollectionContainsValidOrInvalidStringProperty_ShouldNotCallAnyOfTheApiTypeConverterMethods(string stringProperty)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(stringProperty, "some value")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToInt(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToIntNullable(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToUtcDateTimeNullable(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToListOfInts(It.IsAny<string>()), Times.Never());
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToBoolean(It.IsAny<string>()), Times.Never());
        }

        [Test]
        [TestCase("DateTimeNullableProperty")]
        [TestCase("Date_Time_Nullable_Property")]
        [TestCase("date_time_nullable_property")]
        [TestCase("datetimenullableproperty")]
        [TestCase("dateTimeNullableProperty")]
        public void WhenCollectionContainsValidDateTimeProperty_ShouldCallTheToDateTimeNullableMethod(string dateTimePropertyName)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();
            Mock.Get(apiTypeConverterMock).Setup(x => x.ToUtcDateTimeNullable(It.IsAny<string>())).Returns(DateTime.Now).Verifiable();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(dateTimePropertyName, "2016-12-12")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToUtcDateTimeNullable(It.IsAny<string>()), Times.AtLeastOnce());
        }

        [Test]
        [TestCase("invalid date time property name")]
        public void WhenCollectionContainsInvalidDateTimeNullableProperty_ShouldNotCallTheDateTimeNullableMethod(string invalidDateTimeNullablePropertyName)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();
            Mock.Get(apiTypeConverterMock).Setup(x => x.ToUtcDateTimeNullable(It.IsAny<string>())).Verifiable();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(invalidDateTimeNullablePropertyName, "2016-12-12")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToUtcDateTimeNullable(It.IsAny<string>()), Times.Never());
        }

        [Test]
        [TestCase("BooleanNullableStatusProperty")]
        [TestCase("BooleanNullableStatusProperty")]
        [TestCase("Boolean_Nullable_Status_Property")]
        [TestCase("Boolean_Nullable_Status_Property")]
        [TestCase("boolean_nullable_status_property")]
        [TestCase("boolean_nullable_status_property")]
        [TestCase("booleannullablestatusproperty")]
        [TestCase("booleannullablestatusproperty")]
        [TestCase("booLeanNullabLeStaTusProperty")]
        [TestCase("booLeanNullabLeStaTusProperty")]
        public void WhenCollectionContainsValidBooleanStatusPropertyAndPublishedValue_ShouldCallTheToStatusMethod(string booleanStatusPropertyName)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();
            Mock.Get(apiTypeConverterMock).Setup(x => x.ToBoolean(It.IsAny<string>())).Returns(true).Verifiable();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(booleanStatusPropertyName, "some published value")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToBoolean(It.IsAny<string>()), Times.AtLeastOnce());
        }

        [Test]
        [TestCase("invalid boolean property name")]
        public void WhenCollectionContainsInvalidBooleanNullableStatusProperty_ShouldNotCallTheToStatusMethod(string invalidBooleanNullableStatusPropertyName)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();
            Mock.Get(apiTypeConverterMock).Setup(x => x.ToBoolean(It.IsAny<string>())).Verifiable();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(invalidBooleanNullableStatusPropertyName, "true")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToBoolean(It.IsAny<string>()), Times.Never());
        }

        [Test]
        [TestCase("OrderStatus")]
        [TestCase("PaymentStatus")]
        [TestCase("ShippingStatus")]
        [TestCase("order_status")]
        [TestCase("payment_status")]
        [TestCase("shipping_status")]
        [TestCase("orderstatus")]
        [TestCase("paymentstatus")]
        [TestCase("shippingstatus")]
        [TestCase("OrderstaTuS")]
        [TestCase("shiPpiNgStaTus")]
        [TestCase("PAymeNTStatUs")]
        public void WhenCollectionContainsValidNullableEnumProperty_ShouldCallTheToEnumNullableMethod(string enumNullableProperty)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();
            Mock.Get(apiTypeConverterMock).Setup(x => x.ToEnumNullable(It.IsAny<string>(), It.IsAny<Type>())).Returns(null).Verifiable();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(enumNullableProperty, "some enum value")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToEnumNullable(It.IsAny<string>(), It.IsAny<Type>()), Times.AtLeastOnce());
        }

        [Test]
        [TestCase("invalid enum property name")]
        public void WhenCollectionContainsInvalidNullableEnumProperty_ShouldNotCallTheToEnumNullableMethod(string invalidEnumNullableProperty)
        {
            //Arange
            IApiTypeConverter apiTypeConverterMock = Mock.Of<IApiTypeConverter>();
            Mock.Get(apiTypeConverterMock).Setup(x => x.ToEnumNullable(It.IsAny<string>(), It.IsAny<Type>())).Verifiable();

            IObjectConverter objectConverter = new Converters.ObjectConverter(apiTypeConverterMock);

            ICollection<KeyValuePair<string, string>> collection = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(invalidEnumNullableProperty, "some enum value")
            };

            //Act
            objectConverter.ToObject<SomeTestingObject>(collection);

            //Assert
            Mock.Get(apiTypeConverterMock).Verify(x => x.ToEnumNullable(It.IsAny<string>(), It.IsAny<Type>()), Times.Never());
        }
    }   
}