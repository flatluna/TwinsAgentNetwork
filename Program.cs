using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TwinAgentsNetwork.MCPTools;
using TwinAgentsNetwork.Services;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.AzureFunctions;
using TwinFx.Agents;

// Default Azure Functions startup
var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register custom services
builder.Services.AddHttpClient<TwinDataPersonalServices>();
builder.Services.AddScoped<TwinDataPersonalServices>();
builder.Services.AddScoped<AgentTwinPersonalData>();
builder.Services.AddScoped<AgentTwinFamily>(); // Add AgentTwinFamily to DI container
builder.Services.AddScoped<AgentTwinContacts>(); // Add AgentTwinContacts to DI container

// Register MyMemory agent with HttpClient
builder.Services.AddHttpClient<AgentTwinMyMemory>();
builder.Services.AddScoped<AgentTwinMyMemory>(); // Add AgentTwinMyMemory to DI container

// Register ShortMemory agent with HttpClient
builder.Services.AddHttpClient<AgentTwinShortMemory>();
builder.Services.AddScoped<AgentTwinShortMemory>(); // Add AgentTwinShortMemory to DI container

// Register AiWebSearchAgent with HttpClient and IConfiguration
builder.Services.AddHttpClient<AiWebSearchAgent>();
builder.Services.AddScoped<AiWebSearchAgent>(); // Add AiWebSearchAgent to DI container

// Register FoodDietery agent with dependencies
builder.Services.AddScoped<AgentTwinFoodDietery>(); // Add AgentTwinFoodDietery to DI container

// Register AgentNutritionCosmosDB service
builder.Services.AddScoped<AgentNutritionCosmosDB>(); // Add AgentNutritionCosmosDB to DI container

// Register AgentHealthCosmosDB service
builder.Services.AddScoped<AgentHealthCosmosDB>(); // Add AgentHealthCosmosDB to DI container

// Register AgentTwinHealth agent with dependencies
builder.Services.AddScoped<AgentTwinHealth>(); // Add AgentTwinHealth to DI container

// Register AgentTwinNutritionDiaryFx Azure Function
builder.Services.AddScoped<AgentTwinNutritionDiaryFx>(); // Add AgentTwinNutritionDiaryFx to DI container

// Register AgentHealthFx Azure Function
builder.Services.AddScoped<AgentHealthFx>(); // Add AgentHealthFx to DI container

// Register TwinAgentMaster agent with HttpClient
builder.Services.AddHttpClient<TwinAgentMaster>();
builder.Services.AddScoped<TwinAgentMaster>(); // Add TwinAgentMaster to DI container

// Register MiCasa Cosmos DB service
builder.Services.AddScoped<AgentTwinMiCasaCosmosDB>(); // Add AgentTwinMiCasaCosmosDB to DI container

// Register Agenda Customer Cosmos DB service
builder.Services.AddScoped<AgentTwinAgendaCustomerCosmosDB>(); // Add AgentTwinAgendaCustomerCosmosDB to DI container

// Register Communication services
builder.Services.AddScoped<AgentTwinCommunicate>(); // Add AgentTwinCommunicate to DI container
builder.Services.AddScoped<AgentCommunicationCosmosDB>(); // Add AgentCommunicationCosmosDB to DI container

builder.Build().Run();
