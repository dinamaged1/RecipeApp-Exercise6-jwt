using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;


namespace RecipeClient.Pages
{
    public class ImageData
    {
        public string FileName { get; set; } = string.Empty;
        public string Base64String { get; set; }= string.Empty;
    }

    [IgnoreAntiforgeryToken(Order = 1001)]
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IWebHostEnvironment _host;
        public static Configuration? Default { get; }
        [BindProperty]
        public string ApiUrl { get; set; }

        
        public IndexModel(ILogger<IndexModel> logger,IConfiguration config, IWebHostEnvironment host)
        {
            _logger = logger;
            ApiUrl = config.GetRequiredSection("url").Get<string>();
            _host = host;
        }
        public async Task OnPostSaveImageToFolder([FromBody] ImageData recievedImage)
        {
            byte[] imageBytes = Convert.FromBase64String(recievedImage.Base64String);
            MemoryStream msOriginalImage = new MemoryStream(imageBytes, 0,
              imageBytes.Length);

            IImageFormat format;
            MemoryStream msResizedImage = new MemoryStream();
            using (Image image = Image.Load(msOriginalImage, out format))
            {
                List<int> resizingData = ResizeImage(image, 700, 700);
                image.Mutate(x => x.Resize(resizingData[0], resizingData[1]));
                image.Save(msResizedImage, format);
            }
            var data= msResizedImage.ToArray();
            var filePath = $"{_host.WebRootPath}/RecipesImages/{recievedImage.FileName}";
            await System.IO.File.WriteAllBytesAsync(filePath, data);
        }

        public List<int> ResizeImage(SixLabors.ImageSharp.Image img,int maxWidth,int maxHeight)
        {
            List<int> resizingData = new List<int>();
            if(img.Width != maxWidth || img.Height != maxHeight)
            {
                double widthRatio=(double)img.Width / (double)maxWidth;
                double heightRatio=(double)img.Height / (double)maxHeight;
                double ratio=Math.Max(heightRatio, widthRatio);
                int newWidth=(int)(img.Width / ratio);
                int newHeight=(int)(img.Height / ratio);
                resizingData.Add(newWidth);
                resizingData.Add(newHeight);
            }
            else
            {
                resizingData.Add(img.Width);
                resizingData.Add(img.Height);    
            }
            return resizingData;
        }

        public void OnPostDeleteImageFolder([FromBody] ImageData recievedImage)
        {
            //Delete Image from folder RecipeImages
            var filePathDelete = $"{_host.WebRootPath}{recievedImage.FileName}";
            System.IO.File.Delete(filePathDelete);
        }
    }
}