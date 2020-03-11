using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gigya.Microdot.Configuration;
using Newtonsoft.Json;
using NUnit.Framework;
using Shouldly;

namespace Gigya.Microdot.UnitTests.Configuration.Convertor
{
    [TestFixture, Parallelizable(ParallelScope.Fixtures)]
    public class ConvertorConfigTests
    {
        [Test]
        [Description("Check that we can convert a char[]")]
        public void CanConvertStringToCharArray()
        {
            CanConvertStringToTArray(new[] { 'a', 'b', 'c', 'd' });
        }

        [Test]
        [Description("Check that we can convert an int[]")]
        public void CanConvertStringToIntArray()
        {
            CanConvertStringToTArray(new[] {1, 4, 5, 6});
        }

        [Test]
        [Description("Check that we can convert a uint[]")]
        public void CanConvertStringToShortArray()
        {
            CanConvertStringToTArray(new[] { 1u, 4u, 5u, 6u });
        }

        [Test]
        [Description("Check that we can convert a long[]")]
        public void CanConvertStringToLongArray()
        {
            CanConvertStringToTArray(new[] { 1l, 4l, 5l, 6l });
        }

        [Test]
        [Description("Check that we can convert an ulong[]")]
        public void CanConvertStringToULongArray()
        {
            CanConvertStringToTArray(new[] { 1ul, 4ul, 5ul, 6ul });
        }

        [Test]
        [Description("Check that we can convert a float[]")]
        public void CanConvertStringToFloatArray()
        {
            CanConvertStringToTArray(new[] { 1.0f, 4.0f, 5.0f, 6.0f });
        }

        [Test]
        [Description("Check that we can convert a decimal[]")]
        public void CanConvertStringToDecimalArray()
        {
            CanConvertStringToTArray(new[] { 1.0m, 4.0m, 5.0m, 6.0m });
        }

        [Test]
        [Description("Check that we can convert a string[]")]
        public void CanConvertStringToStringArray()
        {
            CanConvertStringToTArray(new[] {"I'm", "a","wonderful","string","enumeration" });
        }

        [Test]
        [Description("Check that we can convert a datetime[]")]
        public void CanConvertStringToDatetimeArray()
        {
            var array = new[]
            {
                new DateTime(2017,11,5),
                new DateTime(2015,7,15),
                new DateTime(2011,1,25),
                new DateTime(2018,3,3),
                new DateTime(2010,9,19),
            };
            CanConvertStringToTArray(array);
        }
        public void CanConvertStringToTArray<T>(T[] enumeration)
        {
            var json = $"{{\n  \"TArray\": \"{string.Join(",",enumeration)}\"\n}}";
            var sut = JsonConvert.DeserializeObject<TArrayConfig<T>>(json,new ConfigJsonConverter());
            sut.TArray.Length.ShouldBe(enumeration.Length);
            for (var i = 0; i < enumeration.Length; i++)
            {
                sut.TArray[i].ShouldBe(enumeration[i]);
            }
        }

    }
}
