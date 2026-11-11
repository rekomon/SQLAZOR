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

            // On-disk stores (chat.db, AI settings, profiles) resolve a writable base
            // directory at startup; IIS app pools should set SQLAZOR:DataDirectory.
            AppDataPath.Configure(builder.Configuration["SQLAZOR:DataDirectory"], builder.Environment.ContentRootPath);

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
            builder.Services.AddSingleton<IAiSettingsStore, FileAiSettingsStore>();
            builder.Services.AddScoped<GenerationState>(sp =>
                new GenerationState(sp.GetRequiredService<IAiSettingsStore>()));
            builder.Services.AddScoped<ISchemaCodeGeneratorService, SchemaCodeGeneratorService>();
            builder.Services.AddScoped<IStoredProcedureGeneratorService, StoredProcedureGeneratorService>();
            builder.Services.AddScoped<ICrudGeneratorService, CrudGeneratorService>();
            builder.Services.AddScoped<IProjectScaffoldGeneratorService, ProjectScaffoldGeneratorService>();
            builder.Services.AddScoped<IGenerateDocumentationService, GenerateDocumentationService>();
            builder.Services.AddSingleton<LookupValueSampler>();
            builder.Services.AddSingleton<IGeneratedCodeValidator, GeneratedCodeValidator>();
            builder.Services.AddSingleton<IChatSessionStore, SqliteChatSessionStore>();
            builder.Services.AddSingleton<IGenerationManifestStore, FileGenerationManifestStore>();
            builder.Services.AddSingleton<IGenerationProfileStore, FileGenerationProfileStore>();
            builder.Services.AddSingleton<IDeepCompileValidator, DotnetBuildValidator>();
            builder.Services.AddHttpClient<IChatProvider, OllamaService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5); // local LLM inference can be slow, especially on first load of a model
            });
            builder.Services.AddHttpClient<IChatProvider, DeepSeekService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            });
            builder.Services.AddHttpClient<IChatProvider, OpenRouterService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            });
            builder.Services.AddSingleton<IChatProviderService, ChatProviderService>();
            


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
