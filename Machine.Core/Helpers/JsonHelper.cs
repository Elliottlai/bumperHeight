using Machine.Core.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{
   public static class JsonHelper
   {
      //   public static void ToJsonFile(this IAxisArgs This, string path)
      //       => This.ToJsonFile(path);

      public static void ToJsonFile(this object This, string path)
      {

         String PathName = Path.GetDirectoryName(path);
         if (!Directory.Exists(PathName))
            Directory.CreateDirectory(PathName);
         using (FileStream fs = File.Open(path, FileMode.Create))
         using (StreamWriter sw = new StreamWriter(fs))
         {
            new JsonSerializer
            {
               Formatting = Formatting.Indented,
               ReferenceLoopHandling = ReferenceLoopHandling.Error
            }
            .Serialize(sw, This);
         }
      }

      public static T Load<T>(string path)
      {
         if (!File.Exists(path))
         {

            throw new FileNotFoundException(string.Format("找不到指定的檔案 {0}", path));

         }

         try
         {
                JsonSerializerSettings settings = new JsonSerializerSettings()
                {
                    DefaultValueHandling = DefaultValueHandling.Populate,
                   TypeNameHandling = TypeNameHandling.All
            };

            using (FileStream fs = File.Open(path, FileMode.Open))
            using (StreamReader sr = new StreamReader(fs))
            using (JsonReader jr = new JsonTextReader(sr))
            {
               JsonSerializer serializer = JsonSerializer.Create(settings);
               return serializer.Deserialize<T>(jr);
            }
         }
         catch (JsonReaderException ex)
         {
            throw ex;
         }
      }
   }
}
