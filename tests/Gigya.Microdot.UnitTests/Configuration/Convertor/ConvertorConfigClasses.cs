using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gigya.Microdot.UnitTests.Configuration.Convertor
{
    public class TArrayConfig<T>
    {
        public T[] TArray;
    }

    public class TArrayPropertyConfig<T>
    {
        public T[] TArrayProperty { get; set; }
    }

    public class NestedConfig<T>
    {
        public TArrayConfig<T> NestedTArrayConfig;
    }

    public class NestedPropertyConfig<T>
    {
        public TArrayPropertyConfig<T> NestedTArrayPropertyConfig;
    }
}
