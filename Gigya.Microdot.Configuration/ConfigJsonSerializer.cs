using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Gigya.Microdot.Configuration
{
    public class ConfigContractResolver : DefaultContractResolver
    {
        private static readonly DefaultContractResolver DefaultContractResolverInstance = new DefaultContractResolver();
        public static ConfigContractResolver Instance = new ConfigContractResolver();


        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var jsonProperty = base.CreateProperty(member, memberSerialization);
            if (jsonProperty.PropertyType.IsArray)
            {
                jsonProperty.Converter = new ConfigJsonConverter();
                return jsonProperty;
            }
            //TODO: make sure to take care of cyclic reference type so not to get stack-overflow
            var contract = ResolveContract(jsonProperty.PropertyType);
            
            return jsonProperty;
        }
    }

    public class ConfigJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer,value);
        }

        private static char[] SplitChars = {','};
        public static object ParseStringToType(string str,Type type)
        {
            var res = TypeDescriptor.GetConverter(type).ConvertFromString(str);
            return res;
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            //TODO:Ugly an messy change it to something more OOP
            if (objectType.IsArray == false && typeof(IList).IsAssignableFrom(objectType) == false)
            {
                JToken valueJToken = (JToken) existingValue;
                Debug.Assert(valueJToken != null);
                return valueJToken.ToObject(objectType);
            }
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    var stringValue = serializer.Deserialize<string>(reader);
                    var elementType = objectType.GetElementType();
                    var objectArray = stringValue.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries).Select(t => ParseStringToType(t.Trim(),elementType)).ToArray();
                    var arr = Array.CreateInstance(elementType, objectArray.Length);
                    Array.Copy(objectArray, arr, objectArray.Length);
                    return arr;
                case JsonToken.StartObject:
                    var jObject = serializer.Deserialize<JObject>(reader);

                    break;
            }

            return null;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType.IsArray;
        }
    }
}
