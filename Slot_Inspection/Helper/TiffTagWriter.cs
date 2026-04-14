using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoupInspecMachine.Helper
{
    static class TiffTagWriter
    {
        public static void WriteTags(string tifPath, string labe11, string labe12)
        {
            var psi = new ProcessStartInfo
            {
                FileName = @"exiftool.exe",
                Arguments = $"-overwrite_original " +
                            $"-Keywords=\"{labe11},{labe12}\" " +
                            $"-XPKeywords=\"{labe11};{labe12}\" " +
                            $"\"{tifPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process.Start(psi)?.WaitForExit();


            //string output = "";



            //var psi1 = new ProcessStartInfo
            //{
            //    FileName = @"exiftool.exe",
            //    // 修改 1: 加入 "-XPKeywords" 或 "-Subject" 參數，讓 ExifTool 只輸出標籤欄位
            //    // 修改 2: 加入 "-s3" 參數，讓輸出只包含數值，不包含欄位名稱 (例如只輸出 "1, 2" 而非 "XPKeywords: 1, 2")
            //    Arguments = $"-Keywords -s3 \"{tifPath}\"",
            //    RedirectStandardOutput = true,
            //    UseShellExecute = false,
            //    CreateNoWindow = true, // 加入這行：執行時不會跳出黑視窗
            //                           //StandardOutputEncoding = System.Text.Encoding.UTF8 // 確保中文標籤不亂碼
            //};

            //using (var p = Process.Start(psi1))
            //{
            //    output = p.StandardOutput.ReadToEnd();
            //    p.WaitForExit();
            //}


            // 修改 3: 去除回傳字串前後的空白與換行
            //return output.Trim();


        }

        //public static string getExtTage(string tifPath)
        //{
        //    string output = "";
        //    try
        //    {
        //        // 檢查檔案是否存在，避免 Process 啟動失敗
        //        if (!File.Exists(tifPath)) return "File not found";

        //        var psi = new ProcessStartInfo
        //        {
        //            FileName = @"exiftool.exe",
        //            // 修改 1: 加入 "-XPKeywords" 或 "-Subject" 參數，讓 ExifTool 只輸出標籤欄位
        //            // 修改 2: 加入 "-s3" 參數，讓輸出只包含數值，不包含欄位名稱 (例如只輸出 "1, 2" 而非 "XPKeywords: 1, 2")
        //            Arguments = $"-XPKeywords -s3 \"{tifPath}\"",
        //            RedirectStandardOutput = true,
        //            UseShellExecute = false,
        //            CreateNoWindow = true, // 加入這行：執行時不會跳出黑視窗
        //                                   //StandardOutputEncoding = System.Text.Encoding.UTF8 // 確保中文標籤不亂碼
        //        };

        //        using (var p = Process.Start(psi))
        //        {
        //            output = p.StandardOutput.ReadToEnd();
        //            p.WaitForExit();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        // 建議紀錄錯誤資訊
        //        return $"Error: {ex.Message}";
        //    }

        //    // 修改 3: 去除回傳字串前後的空白與換行
        //    return output.Trim();
        //}


    }
}
