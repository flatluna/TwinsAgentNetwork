using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Azure.Identity;
using Google.Protobuf;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml; 
using TwinAgentsLibrary.Services;
using TwinAgentsNetwork.Services;
using TwinFx.Services;
using JsonException = System.Text.Json.JsonException;

namespace TwinAgentsNetwork.Agents
{
    public class DocumentsNoStructuredAgent
    {
        private readonly ILogger<DocumentsNoStructuredAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;
        string DeploymentName = "";
        
        public DocumentsNoStructuredAgent(ILogger<DocumentsNoStructuredAgent> logger, IConfiguration configuration, string Model)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");

                // Get Azure OpenAI configuration
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ??
                              configuration["AzureOpenAI:Endpoint"] ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");

                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ??
                            configuration["AzureOpenAI:ApiKey"] ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");

                var deploymentName = Model ?? configuration["Values:AzureOpenAI:DeploymentName"] ??
                                    configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4";
                deploymentName = "gpt-5-mini";

                _logger.LogInformation("🔧 Using Azure OpenAI configuration:");
                _logger.LogInformation("   • Endpoint: {Endpoint}", endpoint);
                _logger.LogInformation("   • Deployment: {DeploymentName}", deploymentName);
                _logger.LogInformation("   • Auth: API Key");
                
                // Initialize Azure OpenAI client with extended timeout (5 minutes)
                var clientOptions = new Azure.AI.OpenAI.AzureOpenAIClientOptions
                {
                    NetworkTimeout = TimeSpan.FromSeconds(600)
                };
                
                _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), clientOptions);
                _chatClient = _azureClient.GetChatClient(deploymentName);

                _logger.LogInformation("✅ Azure OpenAI clients initialized successfully with API Key authentication");
                _logger.LogInformation("✅ Agent Framework ready for document processing");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize DocumentsNoStructuredAgent");
                throw;
            }
        }

        /// <summary>
        /// Extrae datos de documentos no estructurados usando Document Intelligence
        /// </summary>
        public async Task<UnstructuredDocumentResult> ExtractDocumentDataAsync(
            int PaginaIniciaIndice,
            int PaginaTerminaIndice,
            bool TieneIndex,
            bool Translation,
            string Language,
            string twinID,
            string filePath,
            string fileName,
            string CustomerID
             )
        {
            _logger.LogInformation("📄 Starting unstructured document data extraction for: {FileName}", fileName);
            _logger.LogInformation("📂 Container: {Container}, Path: {Path}", twinID, filePath);

            var result = new UnstructuredDocumentResult
            {
                Success = false,
                ContainerName = twinID,
                FilePath = filePath,
                FileName = fileName,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                // STEP 1: Generate SAS URL for Document Intelligence access
                _logger.LogInformation("🔗 STEP 1: Generating SAS URL for document access...");

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinID);
                var fullFilePath = $"{filePath}/{fileName}";
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(2));

                if (string.IsNullOrEmpty(sasUrl))
                {
                    result.ErrorMessage = "Failed to generate SAS URL for document access";
                    _logger.LogError("❌ Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                    return result;
                }

                result.DocumentUrl = sasUrl;
                _logger.LogInformation("✅ SAS URL generated successfully");

                // STEP 2: Extract data using Document Intelligence
                _logger.LogInformation("🤖 STEP 2: Extracting data with Document Intelligence...");

                var documentAnalysis = await _documentIntelligenceService.AnalyzeDocumentWithPagesAsync(sasUrl);

                if (!documentAnalysis.Success)
                {
                    result.ErrorMessage = $"Document Intelligence extraction failed: {documentAnalysis.ErrorMessage}";
                    _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                    return result;
                }

                result.RawTextContent = documentAnalysis.TextContent;
                result.TotalPages = documentAnalysis.TotalPages;
                result.DocumentPages = documentAnalysis.DocumentPages;
                result.Tables = documentAnalysis.Tables;

                _logger.LogInformation("✅ Document Intelligence extraction completed - {Pages} pages, {TextLength} chars",
                    documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

                result.Success = true;
                _logger.LogInformation("✅ Unstructured document extraction completed successfully");
                var Myresults = await AgentAICreateDocument(documentAnalysis, Language);

                Myresults.RootDocument.FilePath = filePath;
                Myresults.RootDocument.FileName = result.FileName;
                Myresults.RootDocument.TotalPages = result.TotalPages;
                Myresults.RootDocument.ProcessedAt = DateTime.UtcNow;
                Myresults.RootDocument.CustomerID = twinID; 
                Myresults.RootDocument.DocumentName = fileName;
                Myresults.RootDocument.DocumentID = Guid.NewGuid().ToString();
                Myresults.RootDocument.TwinID = twinID;
                Myresults.RootDocument.CustomerID = CustomerID; 
                foreach(var document in Myresults.RootDocument.Indice.Secciones)
                {
                    // Create DocumentIndex service to upload sections
                    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                    var indexLogger = loggerFactory.CreateLogger<DocumentIndex>();
                    var documentIndex = new DocumentIndex(indexLogger, _configuration);

                    // Convert DatoExtraido to DatoExtraidoSearchable
                    var datosExtraidos = Myresults.RootDocument.Indice.Datos?
                        .Select(d => new Services.DatoExtraidoSearchable
                        {
                            NombrePropiedad = d.NombrePropiedad,
                            ValorPropiedad = d.ValorPropiedad,
                            Contexto = d.Contexto
                        }).ToList();

                    // Create DocumentIndexContent for each section using Services.DocumentIndexContent
                    var sectionDocument = new Services.DocumentIndexContent
                    {
                        Id = Guid.NewGuid().ToString(),
                        TwinID = Myresults.RootDocument.TwinID,
                        DocumentID = Myresults.RootDocument.DocumentID,
                        CustomerID = Myresults.RootDocument.CustomerID,
                        TituloDocumento = Myresults.RootDocument.tituloDocumento,
                        DocumentName = Myresults.RootDocument.DocumentName,
                        ResumenEjecutivo = Myresults.RootDocument.resumenEjecutivo,
                        TotalPages = Myresults.RootDocument.TotalPages,
                        TotalTokensInput = Myresults.RootDocument.TotalTokensInput,
                        TotalTokensOutput = Myresults.RootDocument.TotalTokensOutput,
                        FilePath = Myresults.RootDocument.FilePath,
                        FileName = Myresults.RootDocument.FileName,
                        ProcessedAt = Myresults.RootDocument.ProcessedAt,
                        Contenido = document.Contenido,
                        Titulo = document.Titulo,
                        Pagina = document.Pagina,
                        DatosExtraidos = datosExtraidos // Add extracted data
                    };

                    // Upload each section document to the index
                    var uploadResult = await documentIndex.UploadDocumentAsync(sectionDocument);

                    if (uploadResult.Success)
                    {
                        _logger.LogInformation("✅ Section uploaded successfully: {Titulo} (Document ID: {DocumentId})", 
                            document.Titulo, uploadResult.DocumentId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to upload section: {Titulo} - Error: {Error}", 
                            document.Titulo, uploadResult.Error);
                    }
                }


                if (Myresults.Success)
                {
                    
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting data from unstructured document {FileName}", fileName);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Responde preguntas sobre un documento específico
        /// </summary>
        public async Task<string> AnswerSearchQuestion(string Idioma, string Question, string TwinID, string FileName)
        {
            try
            {
                if (FileName == "null")
                {
                    FileName = "Global";
                }
                _logger.LogInformation("🔍 Starting search question answering for Question: {Question}, TwinID: {TwinID}, FileName: {FileName}", 
                    Question, TwinID, FileName);

                // Search relevant chapters
                _logger.LogInformation("📚 STEP 1: Searching relevant chapters using DocumentsNoStructuredIndex...");
                var indexLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DocumentsNoStructuredIndex>();
                var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);
                var relevantChapters = await documentsIndex.AnswerSearchUserQuestionAsync(Question, TwinID, FileName);
                
                if (relevantChapters == null || relevantChapters.Count == 0)
                {
                    return @"<div style='padding: 20px; background-color: #f8f9fa; border-left: 4px solid #ffc107; font-family: Arial, sans-serif;'>
                        <h3 style='color: #856404; margin-top: 0;'>🤖 Hola, soy tu Agente Inteligente</h3>
                        <p style='color: #856404;'>No pude encontrar información relevante para tu pregunta en el archivo.</p>
                    </div>";
                }

                _logger.LogInformation("✅ Found {ChapterCount} relevant chapters", relevantChapters.Count);

                // Concatenate chapter content
                _logger.LogInformation("📝 STEP 2: Concatenating chapter content...");
                var contentBuilder = new StringBuilder();
                var fileNames = new HashSet<string>();
                var chapterTitles = new List<string>();

                foreach (var chapter in relevantChapters)
                {
                    if (!string.IsNullOrEmpty(chapter.FileName))
                    {
                        fileNames.Add(chapter.FileName);
                    }

                    if (!string.IsNullOrEmpty(chapter.ChapterTitle))
                    {
                        chapterTitles.Add(chapter.ChapterTitle);
                    }

                    var textToUse = !string.IsNullOrEmpty(chapter.TextSub) ? chapter.TextSub : chapter.TextChapter;
                    if (!string.IsNullOrEmpty(textToUse))
                    {
                        contentBuilder.AppendLine($"\n=== CAPÍTULO: {chapter.ChapterTitle} ===");
                        contentBuilder.AppendLine(textToUse);
                        contentBuilder.AppendLine();
                    }
                }

                var concatenatedContent = contentBuilder.ToString();
                var primaryFileName = fileNames.FirstOrDefault() ?? FileName;

                _logger.LogInformation("📊 Content prepared: {ContentLength} characters", concatenatedContent.Length);

                // Generate AI response
                _logger.LogInformation("🤖 STEP 3: Generating AI response...");
                AIAgent agent = _chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateDocumentAnalysisInstructions(Idioma),
                    name: "DocumentAnalysisAgent",
                    description: "An AI agent specialized in analyzing document content");

                AgentThread thread = agent.GetNewThread();

                var aiPrompt = $@"Responde en {Idioma} sobre el siguiente contenido:
PREGUNTA: {Question}
CONTENIDO: {concatenatedContent}
Sé específico y preciso. No inventes información.";

                var response = await agent.RunAsync(aiPrompt, thread);

                if (string.IsNullOrEmpty(response?.Text))
                {
                    return @"<div style='padding: 20px; background-color: #fff3cd; border-left: 4px solid #ffc107; font-family: Arial, sans-serif;'>
                        <h3 style='color: #856404; margin-top: 0;'>⚠️ Sin respuesta</h3>
                    </div>";
                }

                _logger.LogInformation("✅ AI response generated successfully");
                return response.Text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error answering search question: {Question}", Question);
                return $@"<div style='padding: 20px; background-color: #f8d7da; border-left: 4px solid #dc3545;'>
                    <h3 style='color: #721c24;'>❌ Error del Sistema</h3>
                    <p style='color: #721c24;'>Ocurrió un error al procesar tu pregunta.</p>
                </div>";
            }
        }

        /// <summary>
        /// Responde preguntas sobre todos los documentos
        /// </summary>
        public async Task<string> AnswerSearchAllDocumentsQuestion(string Idioma, string Question, string TwinID)
        {
            try
            {
                _logger.LogInformation("📚 STEP 1: Searching relevant chapters from all documents...");
                var indexLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DocumentsNoStructuredIndex>();
                var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);
                var relevantChapters = await documentsIndex.AnswerSearchUserQuestionAllDocumentsAsync(Question, TwinID);

                if (relevantChapters == null || relevantChapters.Count == 0)
                {
                    return @"<div style='padding: 20px; background-color: #f8f9fa; border-left: 4px solid #ffc107; font-family: Arial, sans-serif;'>
                        <h3 style='color: #856404; margin-top: 0;'>🤖 Hola, soy tu Agente Inteligente</h3>
                        <p style='color: #856404;'>No pude encontrar información relevante en los documentos.</p>
                    </div>";
                }

                _logger.LogInformation("✅ Found {ChapterCount} relevant chapters", relevantChapters.Count);

                // Concatenate content
                _logger.LogInformation("📝 STEP 2: Concatenating chapter content...");
                var contentBuilder = new StringBuilder();
                var fileNames = new HashSet<string>();

                foreach (var chapter in relevantChapters)
                {
                    if (!string.IsNullOrEmpty(chapter.FileName))
                    {
                        fileNames.Add(chapter.FileName);
                    }

                    var textToUse = !string.IsNullOrEmpty(chapter.TextSub) ? chapter.TextSub : chapter.TextChapter;
                    if (!string.IsNullOrEmpty(textToUse))
                    {
                        contentBuilder.AppendLine($"\n=== {chapter.FileName} - {chapter.ChapterTitle} ===");
                        contentBuilder.AppendLine(textToUse);
                    }
                }

                var concatenatedContent = contentBuilder.ToString();
                string primaryFileNames = string.Join(", ", fileNames);

                _logger.LogInformation("🤖 STEP 3: Generating AI response...");
                AIAgent agent = _chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateDocumentAnalysisInstructions(Idioma),
                    name: "DocumentAnalysisAgent",
                    description: "An AI agent specialized in analyzing multiple documents");

                AgentThread thread = agent.GetNewThread();

                var aiPrompt = $@"Responde en {Idioma} sobre los siguientes documentos:
PREGUNTA: {Question}
DOCUMENTOS: {primaryFileNames}
CONTENIDO: {concatenatedContent}";

                var response = await agent.RunAsync(aiPrompt, thread);

                if (string.IsNullOrEmpty(response?.Text))
                {
                    return @"<div style='padding: 20px; background-color: #fff3cd;'>
                        <h3>⚠️ Sin respuesta</h3>
                    </div>";
                }

                _logger.LogInformation("✅ AI response generated successfully");
                return response.Text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error answering search question: {Question}", Question);
                return $@"<div style='padding: 20px; background-color: #f8d7da;'>
                    <h3>❌ Error del Sistema</h3>
                </div>";
            }
        }

        /// <summary>
        /// Procesa DocumentAnalysisResult con AI para extraer índice, resumen ejecutivo y datos estructurados
        /// </summary>
        public async Task<ProcessedDocumentAnalysisResult> ProcessDocumentWithAIAsync(DocumentAnalysisResult documentAnalysis, string idioma = "es")
        {
            _logger.LogInformation("🤖 Starting AI processing of document analysis results");
            _logger.LogInformation("📄 Document has {Pages} pages", documentAnalysis.TotalPages);

            var result = new ProcessedDocumentAnalysisResult
            {
                Success = false,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = documentAnalysis.SourceUri,
                TotalPages = documentAnalysis.TotalPages
            };

            try
            {
                // Prepare document content for AI processing
                var documentContent = documentAnalysis.TextContent ?? string.Empty;
                if (documentAnalysis.DocumentPages?.Count > 0)
                {
                    var pageBuilder = new StringBuilder();
                    foreach (var page in documentAnalysis.DocumentPages)
                    {
                        pageBuilder.AppendLine($"\n--- PÁGINA {page.PageNumber} ---");
                        if (page.LinesText?.Count > 0)
                        {
                            pageBuilder.AppendLine(string.Join("\n", page.LinesText));
                        }
                    }
                    documentContent = pageBuilder.ToString();
                }

                _logger.LogInformation("📝 Document content prepared: {ContentLength} characters", documentContent.Length);

                // Create AI Agent for document analysis
                _logger.LogInformation("🔧 Creating AI Agent for document structure analysis...");
                AIAgent agent = _chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateDocumentStructureAnalysisInstructions(idioma),
                    name: "DocumentStructureAgent",
                    description: "An AI agent specialized in extracting document structure, index, and executive summary");

                // Create a new thread for this analysis
                AgentThread thread = agent.GetNewThread();

                // Build the analysis prompt
                var analysisPrompt = BuildDocumentAnalysisPrompt(documentContent, idioma);

                _logger.LogInformation("🧠 Sending document to AI Agent for analysis...");

                // Run the agent with the prompt
                var response = await agent.RunAsync(analysisPrompt, thread);

                if (string.IsNullOrEmpty(response?.Text))
                {
                    result.ErrorMessage = "AI Agent returned empty response";
                    _logger.LogError("❌ AI Agent returned empty response");
                    return result;
                }

                _logger.LogInformation("✅ AI response received, length: {ResponseLength} characters", response.Text.Length);

                // Parse AI response to extract structured data
                result = ParseAIResponse(response.Text, result, idioma);

                result.Success = true;
                _logger.LogInformation("✅ Document processing completed successfully");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing document with AI");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Crea un documento con índice y contenido usando AI
        /// </summary>
        public async Task<DocumentAIResult> AgentAICreateDocument(DocumentAnalysisResult documentAnalysis, string idioma = "es")
        {
            _logger.LogInformation("🤖 Starting AI document creation with index and content extraction");

            var result = new DocumentAIResult
            {
                Success = false,
                ProcessedAt = DateTime.UtcNow,
                RootDocument = new RootDocument()

            };

            try
            {
                var documentContent = documentAnalysis.TextContent ?? string.Empty;
                if (documentAnalysis.DocumentPages?.Count > 0)
                {
                    var pageBuilder = new StringBuilder();
                    foreach (var page in documentAnalysis.DocumentPages)
                    {
                        pageBuilder.AppendLine($"\n--- PÁGINA {page.PageNumber} ---");
                        if (page.LinesText?.Count > 0)
                        {
                            pageBuilder.AppendLine(string.Join("\n", page.LinesText));
                        }
                    }
                    documentContent = pageBuilder.ToString();
                }

                _logger.LogInformation("📝 Document content prepared: {ContentLength} characters", documentContent.Length);
                var aiPrompt = BuildAIDocumentCreationPrompt(documentContent, idioma);
                
                // Calculate input tokens for the prompt
                var tokenCalculator = new AiTokrens();
                double inputTokens = tokenCalculator.GetTokenCount(aiPrompt);
                _logger.LogInformation("📊 Input tokens calculated: {InputTokens}", inputTokens);

                AIAgent agent = _chatClient.AsIChatClient().CreateAIAgent(
                    instructions: aiPrompt,
                    name: "DocumentCreationAgent",
                    description: "An AI agent specialized in creating document index and extracting content");

                AgentThread thread = agent.GetNewThread();

                _logger.LogInformation("🧠 Sending document to AI Agent for creation...");

                var response = await agent.RunAsync("Respuesta aqui ", thread);

                if (string.IsNullOrEmpty(response?.Text))
                {
                    result.ErrorMessage = "AI Agent returned empty response";
                    _logger.LogError("❌ AI Agent returned empty response");
                    return result;
                }

                _logger.LogInformation("✅ AI response received, length: {ResponseLength} characters", response.Text.Length);

                // Calculate output tokens for the response
                double outputTokens = tokenCalculator.GetTokenCount(response.Text);
                _logger.LogInformation("📊 Output tokens calculated: {OutputTokens}", outputTokens);

                result = ParseAIDocumentResponse(response.Text, result);
                var root = JsonConvert.DeserializeObject<RootDocument>(response.Text);
                root.FilePath = documentAnalysis.SourceUri;
                
                // Assign token counts to RootDocument
                root.TotalTokensInput = inputTokens;
                root.TotalTokensOutput = outputTokens;
                
                result.Success = true;
                _logger.LogInformation("✅ Document creation completed successfully");
                _logger.LogInformation("📈 Tokens - Input: {InputTokens}, Output: {OutputTokens}", inputTokens, outputTokens);
                
                result.RootDocument = root;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating document with AI");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Crea las instrucciones para el agente de creación de documentos
        /// </summary>
        private string CreateAIDocumentCreationInstructions(string idioma)
        {
            var jsonExample = @"
{
    ""indice"": [
        {
            ""indiceName"": ""Section Name"",
            ""paginaDe"": 1,
            ""paginaA"": 5,
            ""contenido"": ""Section content here""
        }
    ],
    ""resumenEjecutivo"": ""Executive summary text here""
}";

            return $@"
🤖 IDENTITY: You are a DOCUMENT CREATION AGENT specialized in extracting document index with content and executive summary.

🎯 YOUR EXPERTISE:
- Expert in document structure identification
- Skilled at extracting document sections and content
- Authority on creating accurate indexes
- Expert in generating executive summaries
- Proficient in {idioma} language

📋 RESPONSE LANGUAGE: {idioma}
- ALWAYS respond in the specified language
- Maintain professional tone

🎨 RESPONSE FORMAT:
You MUST respond with valid JSON only. Example:{jsonExample}

💡 RULES:
1. Extract ALL document sections with their page ranges
2. Include content preview for each section
3. Generate a comprehensive executive summary
4. Response MUST be valid JSON only
5. No markdown code blocks
6. Preserve page numbers";
        }

        /// <summary>
        /// Construye el prompt para la creación de documento
        /// </summary>
        private string BuildAIDocumentCreationPrompt(string documentContent, string idioma)
        {
            return $@"Prompt para Crear un Índice de Contenidos:

Leer el Documento Completo: Dedica tiempo a leer todo el documento para comprender su contenido y propósito general.

Crear un Índice de Contenidos:

Divide el documento en secciones o capítulos según su estructura.
Identifica los subtemas dentro de cada sección.
Organiza la información en el siguiente formato de índice:

I. Título de la Sección 1          TITULO 
II. Título de la Sección 2       TITULO 
Numerar las Páginas (si es necesario): Si el documento tiene páginas numeradas, incluye los números de página junto a cada título y subtema en el índice para facilitar la búsqueda.

Conservar el Texto Completo: Asegúrate de tener una copia del texto completo para referencia futura.
Al final dame un resumen ejecutuvo del documento no me digas tu proceso basado en lo que leiste dame un resumen ejecutuvo ocmpleto del
documento para leer facil. 

En adicion vas a extraer informacion critica del documento tales como 
Nombres personas, direcciones, numeros contratos, para numeros confidenciales solo trael los ultimos 3 digitos,
nombres de instituciones, etc.
Ejemplo en JSON

{{  
  ""indice"": {{  
    ""secciones"": [  
      {{  
        ""titulo"": ""Introducción"",  Crear un titulo para cada seccion y subtemas no uses el numero de pagina
        ""pagina"": 1,  
        ""contenido"": ""Esta sección introduce el tema del documento y establece el contexto general."" 
      }},  
      {{  
        ""titulo"": ""Desarrollo"",  
        ""pagina"": 5,  
        ""contenido"": ""Esta sección presenta el desarrollo del tema.""  
      }},  
      {{  
        ""titulo"": ""Conclusiones"",  
        ""pagina"": 12,  
        ""contenido"": ""En esta sección se presentan las conclusiones del estudio."",  
          
      }}  
    ],
    ""Datos"":[ {{
              ""NombrePropiedad"": ""NombrePersona"",
                ""ValorPropiedad"": ""Juan Pérez"",
                ""Contexto"": ""XXXX""}} ,
                {{NombrePropiedad"": ""NumeroContrato"",
                ""ValorPropiedad"": ""****123"",
                ""Contexto"": ""XXXX""}}
         ]
  }},
 ""resumenEjecutivo"": ""El documento aborda el tema de manera integral, comenzando con una introducción que establece el contexto. El desarrollo profundiza en los aspectos clave, proporcionando análisis detallados y ejemplos relevantes. Finalmente, las conclusiones resumen los hallazgos principales y ofrecen recomendaciones para futuras acciones.""
""tituloDocumento: ""Título del Documento"",
}}  

Esta es la iformacion del documento que tienes que analizar:
IMPORTANTE el inice tiene que tener todos los textos del documento 100%
Al final analiza que todo el documento fue puesto en el ocntenido del indice.
Si omites algo perdemos datos del documento
IMPORTANTE: TRata que los titulos y temas de ttulos no excedan unas 800 palabras cada uno

IDIOMA: {idioma}

DOCUMENTO:

IMPORTANTE: Cuenta las palabras de entrada
***** START DOCUMENT CONTENT *****
{documentContent}
***** END DOCUMENT CONTENT *****
Cuenta las palabras de salida y asegurate que en el contenido del idnice tienes todas las palabras exactas
como viene el texto. No puedes omitir nada
Tu respuesta en JSON debe seguir el formato especificado anteriormente no inventes nada dame solo el INDICE
o tabla de contenidos
 ";
        }

        /// <summary>
        /// Analiza la respuesta del AI para la creación de documento
        /// </summary>
        private DocumentAIResult ParseAIDocumentResponse(string aiResponse, DocumentAIResult result)
        {
            try
            {
                _logger.LogInformation("📊 Parsing AI document response to extract index and summary...");

                string jsonContent = aiResponse;
                if (jsonContent.Contains("```json"))
                {
                    jsonContent = jsonContent.Replace("```json", "").Replace("```", "").Trim();
                }
                else if (jsonContent.Contains("```"))
                {
                    jsonContent = jsonContent.Replace("```", "").Trim();
                }

                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    var root = doc.RootElement;

                    if (root.TryGetProperty("indice", out var indiceElement) && indiceElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in indiceElement.EnumerateArray())
                        {
                            var indiceItem = new DocumentIndiceItem
                            {
                                IndiceName = item.TryGetProperty("indiceName", out var nombre) ? nombre.GetString() ?? string.Empty : string.Empty,
                                PaginaDe = item.TryGetProperty("paginaDe", out var pagDe) ? pagDe.GetInt32() : 0,
                                PaginaA = item.TryGetProperty("paginaA", out var pagA) ? pagA.GetInt32() : 0,
                                Contenido = item.TryGetProperty("contenido", out var contenido) ? contenido.GetString() ?? string.Empty : string.Empty
                            };

                            result.Indice.Add(indiceItem);
                        }
                        _logger.LogInformation("✅ Extracted {ItemCount} index items", result.Indice.Count);
                    }

                    if (root.TryGetProperty("resumenEjecutivo", out var resumen))
                    {
                        result.ResumenEjecutivo = resumen.GetString() ?? string.Empty;
                        _logger.LogInformation("✅ Extracted executive summary: {Length} characters", result.ResumenEjecutivo.Length);
                    }
                }

                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to parse AI response as JSON. Response preview: {ResponsePreview}",
                    aiResponse.Length > 200 ? aiResponse.Substring(0, 200) : aiResponse);
                result.ErrorMessage = $"Failed to parse AI response: {ex.Message}";
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error parsing AI document response");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Crea las instrucciones para el agente de análisis de estructura de documentos
        /// </summary>
        private string CreateDocumentStructureAnalysisInstructions(string idioma)
        {
            return $@"
🤖 IDENTITY: You are an INTELLIGENT DOCUMENT STRUCTURE ANALYSIS AGENT specialized in extracting document index, executive summary, and structured content.

🎯 YOUR EXPERTISE:
- Expert in document structure identification
- Skilled at extracting document hierarchy and index
- Authority on providing accurate executive summaries
- Expert in structured data extraction
- Proficient in {idioma} language

📋 RESPONSE LANGUAGE: {idioma}
- ALWAYS respond in the specified language
- Maintain professional and precise tone

🎨 RESPONSE FORMAT:
You MUST respond with valid JSON only. Example:
{{
    ""indice"": [
        {{
            ""nombre"": ""Section Name"",
            ""paginaDe"": 1,
            ""paginaA"": 5,
            ""contenidoText"": ""Brief preview""
        }}
    ],
    ""resumenEjecutivo"": ""Executive summary here"",
    ""metadatos"": {{
        ""tipoDocumento"": ""type"",
        ""tematicaPrincipal"": ""topic""
    }}
}}

💡 RULES:
1. Extract ALL document sections and their page ranges
2. Generate executive summary (2-3 paragraphs)
3. Response MUST be valid JSON only
4. No markdown code blocks
5. Preserve page numbers";
        }

        /// <summary>
        /// Construye el prompt para el análisis del documento
        /// </summary>
        private string BuildDocumentAnalysisPrompt(string documentContent, string idioma)
        {
            return $@"Analiza el siguiente documento y responde ÚNICAMENTE con JSON válido.

TAREAS:
1. Identifica todas las secciones/capítulos del documento
2. Crea un índice con números de página (paginaDe a paginaA)
3. Extrae contenido de texto de cada sección
4. Genera un resumen ejecutivo (2-3 párrafos)
5. Identifica tipo de documento y temática principal

IDIOMA: {idioma}
DOCUMENTO:
{documentContent}

RESPONDE SOLO CON JSON VÁLIDO.";
        }

        /// <summary>
        /// Analiza la respuesta del AI para extraer datos estructurados
        /// </summary>
        private ProcessedDocumentAnalysisResult ParseAIResponse(string aiResponse, ProcessedDocumentAnalysisResult result, string idioma)
        {
            try
            {
                _logger.LogInformation("📊 Parsing AI response to extract structured data...");

                // Clean response (remove markdown code blocks if present)
                string jsonContent = aiResponse;
                if (jsonContent.Contains("```json"))
                {
                    jsonContent = jsonContent.Replace("```json", "").Replace("```", "").Trim();
                }
                else if (jsonContent.Contains("```"))
                {
                    jsonContent = jsonContent.Replace("```", "").Trim();
                }

                // Parse JSON response
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    var root = doc.RootElement;

                    // Extract índice
                    if (root.TryGetProperty("indice", out var indiceElement) && indiceElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in indiceElement.EnumerateArray())
                        {
                            var indiceItem = new IndiceItem
                            {
                                Titulo = item.TryGetProperty("nombre", out var nombre) ? nombre.GetString() ?? string.Empty : string.Empty,
                                PaginaDe = item.TryGetProperty("paginaDe", out var pagDe) ? pagDe.GetInt32() : 0,
                                PaginaA = item.TryGetProperty("paginaA", out var pagA) ? pagA.GetInt32() : 0,
                                Nivel = item.TryGetProperty("nivel", out var nivel) ? nivel.GetInt32() : 1,
                                Texto = item.TryGetProperty("contenidoText", out var contenido) ? contenido.GetString() ?? string.Empty : string.Empty
                            };

                            result.Indice.Add(indiceItem);
                        }
                        _logger.LogInformation("✅ Extracted {ItemCount} index items", result.Indice.Count);
                    }

                    // Extract resumen ejecutivo
                    if (root.TryGetProperty("resumenEjecutivo", out var resumen))
                    {
                        result.ResumenEjecutivo = resumen.GetString() ?? string.Empty;
                        _logger.LogInformation("✅ Extracted executive summary: {Length} characters", result.ResumenEjecutivo.Length);
                    }

                    // Extract metadatos
                    if (root.TryGetProperty("metadatos", out var metadatos))
                    {
                        result.Metadatos = new DocumentMetadatos
                        {
                            TipoDocumento = metadatos.TryGetProperty("tipoDocumento", out var tipo) ? tipo.GetString() ?? string.Empty : string.Empty,
                            TematicaPrincipal = metadatos.TryGetProperty("tematicaPrincipal", out var tematica) ? tematica.GetString() ?? string.Empty : string.Empty
                        };

                        if (metadatos.TryGetProperty("palabrasClave", out var palabrasElement) && palabrasElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var palabra in palabrasElement.EnumerateArray())
                            {
                                var val = palabra.GetString();
                                if (!string.IsNullOrEmpty(val))
                                {
                                    result.Metadatos.PalabrasClave.Add(val);
                                }
                            }
                        }
                        _logger.LogInformation("✅ Extracted metadata: Type={Type}, Topic={Topic}", result.Metadatos.TipoDocumento, result.Metadatos.TematicaPrincipal);
                    }
                }

                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to parse AI response as JSON. Response preview: {ResponsePreview}", 
                    aiResponse.Length > 200 ? aiResponse.Substring(0, 200) : aiResponse);
                result.ErrorMessage = $"Failed to parse AI response: {ex.Message}";
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error parsing AI response");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Create instructions for the document analysis agent
        /// </summary>
        private string CreateDocumentAnalysisInstructions(string idioma)
        {
            return $@"
🤖 IDENTITY: You are an INTELLIGENT DOCUMENT ANALYSIS AGENT specialized in answering questions about document content.

🎯 YOUR EXPERTISE:
- Expert in document comprehension and analysis
- Skilled at extracting relevant information from text
- Authority on providing accurate answers based on document content
- Expert in context-aware response generation
- Proficient in multiple languages

📋 RESPONSE LANGUAGE: {idioma}
- ALWAYS respond in the specified language
- Maintain professional and friendly tone

🎨 HTML FORMATTING REQUIREMENTS:
1. ALWAYS respond in rich HTML format with inline CSS
2. Use colorful grids, tables, and visual elements
3. Include quality indicators with color coding
4. Use cards, badges, and modern UI elements
5. Include icons and emojis for visual appeal
6. Make it responsive and visually engaging
7. Professional color scheme: Blues (#2c3e50, #3498db), Greens (#27ae60), Reds (#e74c3c)

💡 REMEMBER: Create visually stunning HTML responses enriched with document analysis!";
        }
    }

    #region Result Classes

    /// <summary>
    /// Resultado de la creación de documento con índice y resumen ejecutivo
    /// </summary>
    public class DocumentAIResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ProcessedAt { get; set; }

        public RootDocument RootDocument { get; set; }

        /// <summary>
        /// Lista de items del índice del documento
        /// </summary>
        public List<DocumentIndiceItem> Indice { get; set; } = new();

        /// <summary>
        /// Resumen ejecutivo del documento
        /// </summary>
        public string ResumenEjecutivo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Item del índice de documento con nombre, páginas y contenido
    /// </summary>
    public class DocumentIndiceItem
    {
        /// <summary>
        /// Nombre del item del índice
        /// </summary>
        public string IndiceName { get; set; } = string.Empty;

        /// <summary>
        /// Página de inicio
        /// </summary>
        public int PaginaDe { get; set; }

        /// <summary>
        /// Página de fin
        /// </summary>
        public int PaginaA { get; set; }

        /// <summary>
        /// Contenido del item
        /// </summary>
        public string Contenido { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resultado del procesamiento de documento con AI (Índice, Resumen Ejecutivo, Metadatos)
    /// </summary>
    public class ProcessedDocumentAnalysisResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string SourceUri { get; set; } = string.Empty;
        public int TotalPages { get; set; }

        /// <summary>
        /// Índice jerárquico del documento con nombre, página de inicio, página de fin y contenido
        /// </summary>
        public List<IndiceItem> Indice { get; set; } = new();

        /// <summary>
        /// Resumen ejecutivo del documento (2-3 párrafos)
        /// </summary>
        public string ResumenEjecutivo { get; set; } = string.Empty;

        /// <summary>
        /// Metadatos del documento (tipo, temática, palabras clave)
        /// </summary>
        public DocumentMetadatos Metadatos { get; set; } = new();

        /// <summary>
        /// Get comprehensive summary of processing results
        /// </summary>
        public string GetComprehensiveSummary()
        {
            if (!Success)
            {
                return $"❌ Processing failed: {ErrorMessage}";
            }

            return $"✅ Successfully processed: {SourceUri}\n" +
                   $"📄 Pages: {TotalPages}\n" +
                   $"📖 Index Items: {Indice.Count}\n" +
                   $"📋 Document Type: {Metadatos.TipoDocumento}\n" +
                   $"🎯 Main Topic: {Metadatos.TematicaPrincipal}\n" +
                   $"🏷️ Keywords: {string.Join(", ", Metadatos.PalabrasClave)}\n" +
                   $"📅 Processed: {ProcessedAt:yyyy-MM-dd HH:mm} UTC";
        }
    }

    /// <summary>
    /// Item del índice del documento con nombre, páginas e información de contenido
    /// </summary>
    public class IndiceItem
    {
        /// <summary>
        /// Nombre del capítulo o sección
        /// </summary>
        public string Titulo { get; set; } = string.Empty;

        /// <summary>
        /// Página de inicio del capítulo
        /// </summary>
        public int PaginaDe { get; set; }

        /// <summary>
        /// Página de fin del capítulo
        /// </summary>
        public int PaginaA { get; set; }

        /// <summary>
        /// Nivel jerárquico (1=principal, 2=subsección, etc.)
        /// </summary>
        public int Nivel { get; set; } = 1;

        /// <summary>
        /// Contenido de texto preview del capítulo
        /// </summary>
        public string Texto { get; set; } = string.Empty;
    }

    /// <summary>
    /// Metadatos del documento extraído por AI
    /// </summary>
    public class DocumentMetadatos
    {
        /// <summary>
        /// Tipo de documento (contrato, informe, manual, etc.)
        /// </summary>
        public string TipoDocumento { get; set; } = string.Empty;

        /// <summary>
        /// Temática principal del documento
        /// </summary>
        public string TematicaPrincipal { get; set; } = string.Empty;

        /// <summary>
        /// Palabras clave del documento
        /// </summary>
        public List<string> PalabrasClave { get; set; } = new();
    }

    /// <summary>
    /// Resultado del procesamiento de documentos no estructurados
    /// </summary>
    public class UnstructuredDocumentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? DocumentUrl { get; set; }
        public string RawTextContent { get; set; } = string.Empty;
        public int TotalPages { get; set; }
        public List<DocumentPage> DocumentPages { get; set; } = new();
        public List<ExtractedTable> Tables { get; set; } = new();
        public DateTime ProcessedAt { get; set; }

        // AI Processing Results
        public ExtractedContentData ExtractedContent { get; set; } = new();
        public StructuredDocumentData StructuredData { get; set; } = new();
        public List<DocumentInsightData> KeyInsights { get; set; } = new();
        public string ExecutiveSummary { get; set; } = string.Empty;
        public string HtmlOutput { get; set; } = string.Empty;
        public string? RawAIResponse { get; set; }

        public List<CapituloExtraido> ExtractedChapters { get; set; } = new();

        public string FullPath => $"{ContainerName}/{FilePath}/{FileName}";

        public string GetComprehensiveSummary()
        {
            if (!Success)
            {
                return $"❌ Processing failed: {ErrorMessage}";
            }

            return $"✅ Successfully processed: {FileName}\n" +
                   $"📍 Location: {FullPath}\n" +
                   $"📄 Pages: {TotalPages}\n" +
                   $"📊 Tables: {Tables.Count}\n" +
                   $"💡 Insights: {KeyInsights.Count}";
        }
    }

    public class ExtractedContentData
    {
        public string MainTopic { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public List<string> KeyDates { get; set; } = new();
        public List<string> KeyNames { get; set; } = new();
        public List<string> KeyNumbers { get; set; } = new();
        public List<string> KeyAddresses { get; set; } = new();
        public List<string> KeyPhones { get; set; } = new();
        public List<string> KeyEmails { get; set; } = new();
        public List<ImportantSectionData> ImportantSections { get; set; } = new();
        public bool TieneIndice { get; set; } = false;
        public List<CapituloIndice> Indice { get; set; } = new();
        public string Observaciones { get; set; } = string.Empty;
        public string EstructuraDetectada { get; set; } = string.Empty;
    }

    public class ImportantSectionData
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int Page { get; set; }
    }

    public class StructuredDocumentData
    {
        public string Summary { get; set; } = string.Empty;
        public DocumentEntitiesData Entities { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    public class DocumentEntitiesData
    {
        public List<string> Organizations { get; set; } = new();
        public List<string> People { get; set; } = new();
        public List<string> Locations { get; set; } = new();
        public List<string> Dates { get; set; } = new();
        public List<string> Amounts { get; set; } = new();
    }

    public class DocumentInsightData
    {
        public string Insight { get; set; } = string.Empty;
        public string Importance { get; set; } = "MEDIUM";
        public string Category { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class CapituloIndice
    {
        public string Titulo { get; set; } = string.Empty;
        public int PaginaDe { get; set; }
        public int PaginaA { get; set; }
        public int Nivel { get; set; } = 1;
        public string NumeroCapitulo { get; set; } = string.Empty;
    }

    public class CapituloExtraido
    {
        public string Estructura { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string CapituloID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string DocumentID { get; set; } = string.Empty;
        public string NumeroCapitulo { get; set; } = string.Empty;
        public int PaginaDe { get; set; }
        public int PaginaA { get; set; }
        public int Nivel { get; set; } = 1;
        public int TotalTokens { get; set; }
        public string TextoCompleto { get; set; } = string.Empty;
        public string ResumenEjecutivo { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
    public class RootDocument
    {
        [JsonProperty("indice")]
        public ContenidoDocumento Indice { get; set; }
        public string resumenEjecutivo { get; set; } = string.Empty; 

        public string tituloDocumento { get; set; } = string.Empty;
        public string DocumentName { get; set; }
        public int TotalPages { get; set; }
        public double TotalTokensInput { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public double TotalTokensOutput { get; set; }
        public string TwinID { get; set; }
        public string DocumentID { get; set; }
        public string CustomerID { get; set; }
    }
    public class DocumentIndexContent
    {
        [JsonProperty("indice")]
        public ContenidoDocumento Indice { get; set; }
        public string resumenEjecutivo { get; set; } = string.Empty;

        public string tituloDocumento { get; set; } = string.Empty;
        public string DocumentName { get; set; }
        public int TotalPages { get; set; }
        public double TotalTokensInput { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public double TotalTokensOutput { get; set; }
        public string TwinID { get; set; }
        public string DocumentID { get; set; }
        public string CustomerID { get; set; }

        [JsonProperty("titulo")]
        public string Titulo { get; set; }

        [JsonProperty("pagina")]
        public int Pagina { get; set; }

        [JsonProperty("contenido")]
        public string Contenido { get; set; }
    }

    public class ContenidoDocumento
    {
        [JsonProperty("secciones")]
        public List<Seccion> Secciones { get; set; }

        [JsonProperty("datos")]
        public List<DatoExtraido> Datos { get; set; } = new();
    }

    public class Seccion
    {
        [JsonProperty("titulo")]
        public string Titulo { get; set; }

        [JsonProperty("pagina")]
        public int Pagina { get; set; }

        [JsonProperty("contenido")]
        public string Contenido { get; set; }
    }

    /// <summary>
    /// Dato extraído del documento (nombres, direcciones, números de contrato, etc.)
    /// </summary>
    public class DatoExtraido
    {
        /// <summary>
        /// Nombre de la propiedad del dato (e.g., "NombrePropietario", "DireccionPropietario", "NumeroContrato")
        /// </summary>
        [JsonProperty("NombrePropiedad")]
        public string NombrePropiedad { get; set; } = string.Empty;

        /// <summary>
        /// Valor de la propiedad extraída del documento
        /// </summary>
        [JsonProperty("ValorPropiedad")]
        public string ValorPropiedad { get; set; } = string.Empty;

        /// <summary>
        /// Contexto o texto tal cual aparece en el documento
        /// </summary>
        [JsonProperty("Contexto")]
        public string Contexto { get; set; } = string.Empty;
    }
    #endregion
}
