using MudBlazor;
using MudBlazor.Services;
using SQLAZOR.Portal.Components;
using SQLAZOR.Services;


namespace SQLAZOR.Portal
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();


            builder.Services.AddMudServices(config =>
            {
                config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;

                config.SnackbarConfiguration.PreventDuplicates = false;
                config.SnackbarConfiguration.NewestOnTop = false;
                config.SnackbarConfiguration.ShowCloseIcon = true;
                config.SnackbarConfiguration.VisibleStateDuration = 1000;
                config.SnackbarConfiguration.HideTransitionDuration = 500;
                config.SnackbarConfiguration.ShowTransitionDuration = 500;
                config.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Filled;
            });


            builder.Services.AddScoped<ISchemaReaderService, SchemaReaderService>();
            builder.Services.AddScoped<GenerationState>();
            builder.Services.AddScoped<ISchemaCodeGeneratorService, SchemaCodeGeneratorService>();
            builder.Services.AddScoped<IStoredProcedureGeneratorService, StoredProcedureGeneratorService>();
            builder.Services.AddScoped<ICrudGeneratorService, CrudGeneratorService>();
            builder.Services.AddScoped<IProjectScaffoldGeneratorService, ProjectScaffoldGeneratorService>();
            builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5); // local LLM inference can be slow, especially on first load of a model
            });



            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
