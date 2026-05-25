using System;
using System.IO;

namespace FoupInspecMachine.Helper
{
    public static class ImagePathHelper
    {
        public enum ImageType
        {
            Original,
            Draw
        }

        /// <summary>
        /// 產生圖片儲存完整路徑並自動建立資料夾。
        /// 路徑格式：D:\{yyyyMMdd}\{barcode}\Original_image\ 或 Draw_image\{fileName}
        /// </summary>
        /// <param name="barcode">Barcode 編號，作為第三層資料夾名稱</param>
        /// <param name="fileName">檔案名稱（含副檔名），例如 "Left.tiff"</param>
        /// <param name="type">圖片類型：Original 或 Draw</param>
        /// <returns>完整檔案路徑字串</returns>
        public static string GetImagePath(string barcode, string fileName, ImageType type)
        {
            string dateFolder = DateTime.Now.ToString("yyyyMMdd");
            string subFolder  = type == ImageType.Original ? "Original_image" : "Draw_image";
            string dir        = Path.Combine(@"D:\Slot_Bumper_Image", dateFolder, barcode, subFolder);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return Path.Combine(dir, fileName);
        }
    }
}
