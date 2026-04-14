using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Helpers
{
    public static class EnumHelper
    {

 

        public static int ToInt(this Enum e)
        {
            return e.GetHashCode();
        }

        public static String Description(this Enum val)
        {
            /*
             
               return val.GetType()
                      .GetFields()
                      .FirstOrDefault(i => i.Name.Equals(val.ToString()))
                      .GetCustomAttributes(typeof(DescriptionAttribute), false)
                      .OfType<DescriptionAttribute>()
                      .FirstOrDefault().Description ?? string.Empty;

            DescriptionAttribute[] attributes = (DescriptionAttribute[])val
               .GetType()
               .GetField(val.ToString())
               .GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : string.Empty;
            */
            return "";
        }


    }
}
