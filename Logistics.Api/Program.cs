using Logistics.Api.Infrastructure;
using tiny.WebApi.Configurations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularUI", policy =>
    {
        policy.WithOrigins("http://localhost:5100") // Explicitly trust the Angular port
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Enable custom controllers for our algorithm
builder.Services.AddControllers();

// 1. Initialize tiny.webapi core configurations
builder.Services.AddTinyWebApi(new TinyWebApiConfigurations()
{
    ConfigurationDirectoryPath = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName,
    ConnectionStringJSONFileNameWithoutExtension = "connectionstring",
    MailerJSONFileNameWithoutExtension = "mailer",
    QueriesJSONFileNameWithoutExtension = "queries",
    RunAsUserJSONFileNameWithoutExtension = "users",
    DatabaseSpecifications = [],
    MailerSpecifications = [],
    QuerySpecifications = [],
    RunAsUserSpecifications = []
}, null);

WebApplication app = builder.Build();

// 2. Fire the Auto-Provisioning Engine
DatabaseProvisioner.EnsureDatabaseAndTableExist();

app.UseRouting();
app.UseCors("AllowAngularUI");

app.UseAuthorization();
app.MapControllers();
app.UseTinyWebApi(app.Environment);

app.Run();