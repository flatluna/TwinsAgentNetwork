using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TwinAgentsNetwork.Agents
{
    public class TwinAgentMaster
    {
        private readonly HttpClient _httpClient;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;
        private readonly AgentTwinShortMemory _agentTwinShortMemory;
        private readonly AgentTwinFoodDietery _agentTwinFoodDietery;

        public TwinAgentMaster()
        {
            _httpClient = new HttpClient();
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
            _agentTwinShortMemory = new AgentTwinShortMemory(_httpClient);
            _agentTwinFoodDietery = null;
        }

        public TwinAgentMaster(HttpClient httpClient, AgentTwinShortMemory agentTwinShortMemory, AgentTwinFoodDietery agentTwinFoodDietery)
        {
            _httpClient = httpClient;
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
            _agentTwinShortMemory = agentTwinShortMemory ?? new AgentTwinShortMemory(httpClient);
            _agentTwinFoodDietery = agentTwinFoodDietery;
        }

        /// <summary>
        /// Detects user intention and executes the appropriate agent
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="userMessage">User's message or question</param>
        /// <param name="currentAgentName">Current agent name</param>
        /// <param name="numberMessage">Message number in conversation</param>
        /// <returns>Result with agent response</returns>
        public async Task<AgentExecutionResult> DetectAgentIntentionAsync(string twinId, 
            string userMessage,
            string currentAgentName,
            int numberMessage)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new AgentExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Twin ID is required",
                    TwinId = twinId,
                    MessageNumber = numberMessage + 1
                };
            }

            if (string.IsNullOrEmpty(userMessage))
            {
                return new AgentExecutionResult
                {
                    Success = false,
                    ErrorMessage = "User message is required",
                    TwinId = twinId,
                    MessageNumber = numberMessage + 1
                };
            }

            try
            {
                // Step 1: If agent is already known, execute it directly
                if (!string.IsNullOrEmpty(currentAgentName))
                {
                    int newMessageNumber = numberMessage + 1;
                    return await ExecuteAgentByName(twinId, userMessage, currentAgentName, newMessageNumber);
                }

                // Step 2: If no agent is known, detect the intention
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateIntentionDetectionInstructions(userMessage),
                    name: "IntentionDetector");

                var thread = agent.GetNewThread();
                var response = await agent.RunAsync(" Analiza el mensaje que se te dio"  );

                var jsonText = response.Text ?? "{}";
                
                var intentionResult = JsonSerializer.Deserialize<AgentIntentionResult>(jsonText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (intentionResult == null || string.IsNullOrEmpty(intentionResult.AgentName))
                {
                    int newMessageNumber = numberMessage + 1;
                    return new AgentExecutionResult
                    {
                        Success = false,
                        ErrorMessage = "No agent detected",
                        TwinId = twinId,
                        MessageNumber = newMessageNumber,
                        ResponsePrompt = "?? No encontramos el agente que buscas. Por favor, cuéntanos con más detalle qué necesitas. ¿Deseas información sobre: Datos Personales, Mi Familia, Contactos, Fotos Familiares, Documentos, o Mis Memorias?"
                    };
                }

                // Step 3: First message - Agent detected
                int newMsgNumber = numberMessage + 1;
                return new AgentExecutionResult
                {
                    Success = true,
                    TwinId = twinId,
                    AgentName = intentionResult.AgentName,
                    MessageNumber = newMsgNumber,
                    ResponsePrompt = $"? {intentionResult.Message}**. "
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error detecting agent intention: {ex.Message}");
                return new AgentExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Error detecting intention: {ex.Message}",
                    TwinId = twinId,
                    MessageNumber = numberMessage + 1
                };
            }
        }

        private async Task<AgentExecutionResult> ExecuteAgentByName(string twinId, string userMessage, string agentName, int numberMessage)
        {
            if (string.IsNullOrEmpty(agentName))
            {
                return new AgentExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Agent name is required",
                    TwinId = twinId,
                    MessageNumber = numberMessage
                };
            }

            switch (agentName.ToLower())
            {
                case "mis memorias":
                case "short memory":
                case "shortmemory":
                    return await ExecuteShortMemoryAgent(twinId, userMessage, numberMessage);

                case "mi alimentación":
                case "mi alimentacion":
                case "food dietary":
                case "fooddiary":
                    return await ExecuteFoodDietaryAgent(twinId, userMessage, numberMessage);

                default:
                    return new AgentExecutionResult
                    {
                        Success = false,
                        ErrorMessage = $"Agent '{agentName}' not yet implemented",
                        TwinId = twinId,
                        AgentName = agentName,
                        MessageNumber = numberMessage
                    };
            }
        }

        private async Task<AgentExecutionResult> ExecuteShortMemoryAgent(string twinId, string userMessage, int numberMessage)
        {
            try
            {
                var result = await _agentTwinShortMemory.SearchShortMemoryWithAIAsync(
                    twinId,
                    userMessage,
                    "English",
                    null);

                if (!result.Success)
                {
                    return new AgentExecutionResult
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        TwinId = twinId,
                        AgentName = "Mis Memorias",
                        MessageNumber = numberMessage
                    };
                }

                // Convert HTML to plain text
                string plainText = RemoveHtmlTags(result.AIAnalysisHtml);

                string responsePrompt = $"?? :\n\n{plainText}";

                return new AgentExecutionResult
                {
                    Success = true,
                    TwinId = twinId,
                    AgentName = "Mis Memorias",
                    ResponsePrompt = responsePrompt,
                    MessageNumber = numberMessage
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error executing Short Memory agent: {ex.Message}");
                return new AgentExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Error executing Short Memory agent: {ex.Message}",
                    TwinId = twinId,
                    AgentName = "Mis Memorias",
                    MessageNumber = numberMessage
                };
            }
        }

        private async Task<AgentExecutionResult> ExecuteFoodDietaryAgent(string twinId, string userMessage, int numberMessage)
        {
            try
            {
                if (_agentTwinFoodDietery == null)
                {
                    return new AgentExecutionResult
                    {
                        Success = false,
                        ErrorMessage = "Food Dietary agent is not initialized",
                        TwinId = twinId,
                        AgentName = "Mi Alimentación",
                        MessageNumber = numberMessage
                    };
                }

                DateTime currentDate = DateTime.Now;
                int year = currentDate.Year;
                int month = currentDate.Month;
                int day = currentDate.Day;

                var result = await _agentTwinFoodDietery.GetNutritionAnswerAsync(
                    twinId,
                    year,
                    month,
                    day,
                    userMessage,
                    null);

                if (!result.Success)
                {
                    return new AgentExecutionResult
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        TwinId = twinId,
                        AgentName = "Mi Alimentación",
                        MessageNumber = numberMessage
                    };
                }

                // Convert HTML to plain text
                string plainText = RemoveHtmlTags(result.AIResponse);

                string responsePrompt = $"??? Esta es la respuesta del agente de Nutrición:\n\n{plainText}";

                return new AgentExecutionResult
                {
                    Success = true,
                    TwinId = twinId,
                    AgentName = "Mi Alimentación",
                    ResponsePrompt = responsePrompt,
                    MessageNumber = numberMessage
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error executing Food Dietary agent: {ex.Message}");
                return new AgentExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Error executing Food Dietary agent: {ex.Message}",
                    TwinId = twinId,
                    AgentName = "Mi Alimentación",
                    MessageNumber = numberMessage
                };
            }
        }

        private string RemoveHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            var tagRegex = new System.Text.RegularExpressions.Regex(@"<[^>]+>");
            string plainText = tagRegex.Replace(html, "");
            plainText = System.Net.WebUtility.HtmlDecode(plainText);
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\s+", " ").Trim();
            
            return plainText;
        }

        private string CreateIntentionDetectionInstructions(string Message)
        {
            return @" 

Eres un experto Agente Maestro que detectara el nombre del Agente que quermos.
El usuario tiene que darte el nombre como ejemplo 'Queiro hablar con el agente  de Short Memory'
Agentes disponibles (NOMBRES EXACTOS; debes devolver uno de estos o null):
Estos son los nombres a enviar 
Datos-Personales
Mi-Familia
Contactos
Fotos-Familiares
Documentos-Estructurados
Documentos-Semi-estructurados
DocumentosNo-Estructurados
Mis-Memorias
Short-Mmeory
 

Busca palabras claves que se parezcan a 'Datos-Personales
Mi-Familia
Contactos
Fotos-Familiares
Documentos-Estructurados
Documentos-Semi-estructurados
DocumentosNo-Estructurados
Mis-Memorias
Short-Mmeory'
en el mensaje del usuario para detectar el agente.
Formato JSON de ejemplo (cuando se detecta un agente):
{
""NombreAgente"": ""Datos Personales"",
""confidence"": 0.95,
""message"": ""Que te gustaria preguntarle al agente de Datos Personales?"",
""reason"": ""Si encontre palabras similares a Datos Personales""
}

Formato JSON de ejemplo (cuando NO se detecta agente o se necesita aclaración):
{
""NombreAgente"": null,
""confidence"": 0.0,
""message"": ""Por favor dime con qué agente quieres hablar: Datos Personales, Mi Familia, Contactos, Fotos Familiares, Documentos, o Mis Memorias"",
""reason"": ""No se encontró un nombre de agente ni palabras clave suficientes en el mensaje.""
}

Reglas críticas (cumplir estrictamente):

SIEMPRE retornar JSON válido y SOLO el objeto JSON (sin explicaciones ni texto extra).
Los valores de ""NombreAgente"" deben coincidir EXACTAMENTE con uno de los nombres listados arriba, o ser null.
No inventes nombres de agentes ni transformes intencionadamente los nombres a otros no listados. 
 ."

+ " Este es el mensaje del usario usalo para detectar el nombre del Agente:  *** STARTS HERE :" +
Message;
        }
    }

    #region Data Models

    /// <summary>
    /// Result of agent intention detection
    /// /// </summary>
    public class AgentIntentionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string? AgentName { get; set; }
        public double Confidence { get; set; }
        public string? Reason { get; set; }

        public string? Message { get; set; }
    }

    /// <summary>
    /// Result of agent execution
    /// /// </summary>
    public class AgentExecutionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string? AgentName { get; set; }
        public string? ResponsePrompt { get; set; }
        public int MessageNumber { get; set; }
    }

    #endregion
}
