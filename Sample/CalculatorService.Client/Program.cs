using System;
using CalculatorService.Interface;
using Gigya.Microdot.Configuration;
using Gigya.Microdot.Logging.NLog;
using Gigya.Microdot.Ninject;
using Newtonsoft.Json;
using Ninject;

namespace CalculatorService.Client
{
    public class Bar
    {
        public Foo MyFoo { get; set; }
    }
    public class Foo
    {
        public string[] Stuff;
    }
    
    class Program
    {

        static void Main(string[] args)
        {
            var barJson = "{\r\n  \"MyFoo\":\r\n  {\r\n    \"Stuff\":\"those,are,my,stuff\"\r\n  }\r\n}";
            var res = JsonConvert.DeserializeObject<Bar>(barJson, new ConfigJsonConverter());
            try
            {
                Environment.SetEnvironmentVariable("GIGYA_CONFIG_ROOT", Environment.CurrentDirectory);
                Environment.SetEnvironmentVariable("GIGYA_CONFIG_PATHS_FILE", "");
                Environment.SetEnvironmentVariable("GIGYA_ENVVARS_FILE", Environment.CurrentDirectory);
                Environment.SetEnvironmentVariable("REGION", "us1");
                Environment.SetEnvironmentVariable("ZONE", "us1a");
                Environment.SetEnvironmentVariable("ENV", "dev");
                Environment.SetEnvironmentVariable("GIGYA_BASE_PATH", Environment.CurrentDirectory);

                using (var microdotInitializer = new MicrodotInitializer("CalculatorService.Client", new NLogModule()))
                {
                    var calculatorService = microdotInitializer.Kernel.Get<ICalculatorService>();
                    int sum = calculatorService.Add(2, 3).Result;
                    Console.WriteLine($"Sum: {sum}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }
    }
}
