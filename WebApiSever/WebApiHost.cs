using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebApiSever
{
    public class WebApiHost
    {
        private WebApplication? _app;
        private readonly string _url;

        public WebApiHost(string url = "http://0.0.0.0:8069")
        {
            _url = url;
        }

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateBuilder();

            builder.WebHost.UseUrls(_url);

            builder.Services.AddControllers()
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver =
                        new Newtonsoft.Json.Serialization.DefaultContractResolver();
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "My API", Version = "v1" });
            });

            _app = builder.Build();

            // 改為永遠啟用 Swagger（或依需求調整）
            _app.UseSwagger();
            _app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API v1"));

            _app.MapControllers();

            await _app.StartAsync();
        }

        public async Task StopAsync()
        {
            if (_app != null)
                await _app.StopAsync();
        }
    }
}