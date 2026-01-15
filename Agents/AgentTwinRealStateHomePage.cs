using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente inteligente que diseña home pages profesionales para agentes de ventas de casas
    /// </summary>
    public class AgentTwinRealStateHomePage
    {
        private readonly ILogger<AgentTwinRealStateHomePage> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;

        public AgentTwinRealStateHomePage(ILogger<AgentTwinRealStateHomePage> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ??
                              configuration["AzureOpenAI:Endpoint"] ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");

                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ??
                            configuration["AzureOpenAI:ApiKey"] ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");

                var deploymentName = "gpt-5-mini";

                _logger.LogInformation("🔧 AgentTwinRealStateHomePage initialized with deployment: {DeploymentName}", deploymentName);

                var clientOptions = new AzureOpenAIClientOptions
                {
                    NetworkTimeout = TimeSpan.FromSeconds(120)
                };

                _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), clientOptions);
                _chatClient = _azureClient.GetChatClient(deploymentName);

                _logger.LogInformation("✅ AgentTwinRealStateHomePage initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize AgentTwinRealStateHomePage");
                throw;
            }
        }

        /// <summary>
        /// Genera una página home profesional para un agente de bienes raíces
        /// Obtiene los datos del agente desde Cosmos DB usando MicrosoftOID
        /// </summary>
        /// <param name="twinID">ID del twin</param>
        /// <param name="microsoftOID">Microsoft Object ID del agente</param>
        /// <param name="descripcionAgente">Descripción adicional del agente y sus requisitos para la página</param>
        /// <returns>HTML completo de la página home</returns>
        public async Task<RealStateHomePageResult> GenerateHomePageAsync(
            string twinID,
            string microsoftOID,
            string descripcionAgente = "")
        {
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinID))
                {
                    throw new ArgumentException("TwinID es requerido");
                }

                if (string.IsNullOrEmpty(microsoftOID))
                {
                    throw new ArgumentException("MicrosoftOID es requerido");
                }

                _logger.LogInformation("🏠 Generando home page para TwinID: {TwinID}, MicrosoftOID: {MicrosoftOID}", 
                    twinID, microsoftOID);

                // Obtener datos del agente desde Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var cosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);

                var agenteResult = await cosmosDB.GetAgenteInmobiliarioByMicrosoftOIDAsync(microsoftOID);

                if (!agenteResult.Success || agenteResult.Agente == null)
                {
                    throw new Exception($"No se pudo obtener los datos del agente: {agenteResult.ErrorMessage}");
                }

                var agente = agenteResult.Agente;

                _logger.LogInformation("✅ Datos del agente obtenidos: {Nombre}", agente.NombreEquipoAgente);

                // Crear el prompt con los datos del agente
                var prompt = CreateHomePagePrompt(agente, descripcionAgente);

                _logger.LogInformation("🤖 Enviando solicitud a OpenAI...");

                var message = ChatMessage.CreateUserMessage(prompt);
                var chatOptions = new ChatCompletionOptions();

                var response = await _chatClient.CompleteChatAsync(new[] { message }, chatOptions);

                if (response?.Value?.Content?.Count == 0)
                {
                    throw new Exception("Empty response from OpenAI");
                }

                var aiResponse = response.Value.Content[0].Text;
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("✅ Home page generada en {ProcessingTime}ms", processingTime);

                var result = JsonConvert.DeserializeObject<RealStateHomePageResult>(aiResponse);

                if (result != null)
                {
                    result.Success = true;
                    result.ProcessingTimeMs = processingTime;
                    result.TwinID = twinID;
                    result.MicrosoftOIDRSA = microsoftOID;
                    result.id = agente.Id;

                    // Guardar en Cosmos DB
                    try
                    {
                        var homePageLogger = loggerFactory.CreateLogger<AgentTwinRealStateHomePageCosmosDB>();
                        var homePageCosmosDB = new AgentTwinRealStateHomePageCosmosDB(homePageLogger, _configuration);
                        
                        // Verificar si ya existe una HomePage para este agente
                        _logger.LogInformation("🔍 Verificando si ya existe una HomePage para TwinID: {TwinID}, MicrosoftOIDRSA: {MicrosoftOIDRSA}", 
                            twinID, microsoftOID);
                        
                        var existingHomePageResult = await homePageCosmosDB.GetRealStateHomePageAsync(twinID, microsoftOID);
                        
                        if (existingHomePageResult.Success && existingHomePageResult.HomePage != null)
                        {
                            // Ya existe una HomePage, eliminarla primero
                            _logger.LogInformation("⚠️ Se encontró una HomePage existente con ID: {DocumentId}. Eliminando...", 
                                existingHomePageResult.HomePage.id);
                            
                            var deleteResult = await homePageCosmosDB.DeleteRealStateHomePageAsync(
                                existingHomePageResult.HomePage.id, 
                                twinID);
                            
                            if (deleteResult.Success)
                            {
                                _logger.LogInformation("✅ HomePage anterior eliminada exitosamente. Procediendo a guardar la nueva...");
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ No se pudo eliminar la HomePage anterior: {Error}. Intentando guardar la nueva de todas formas...", 
                                    deleteResult.ErrorMessage);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("ℹ️ No se encontró una HomePage existente. Creando nueva...");
                        }
                        
                        // Ahora guardar la nueva HomePage (generar nuevo ID)
                        result.id = ""; // Limpiar el ID para que se genere uno nuevo
                        var saveResult = await homePageCosmosDB.SaveRealStateHomePageAsync(result);
                        
                        if (saveResult.Success)
                        {
                            _logger.LogInformation("✅ HomePage guardada en Cosmos DB con ID: {DocumentId}", saveResult.DocumentId);
                            result.id = saveResult.DocumentId; // Update with the saved document ID
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ No se pudo guardar la HomePage en Cosmos DB: {Error}", saveResult.ErrorMessage);
                            // Continue - don't fail the entire operation
                        }
                    }
                    catch (Exception saveEx)
                    {
                        _logger.LogWarning(saveEx, "⚠️ Error guardando HomePage en Cosmos DB: {Message}", saveEx.Message);
                        // Continue - don't fail the entire operation
                    }
                }

                return result ?? new RealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "Failed to parse OpenAI response",
                    TwinID = twinID,
                    MicrosoftOIDRSA = microsoftOID
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generando home page");
                return new RealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                    TwinID = twinID ?? "",
                    MicrosoftOIDRSA = microsoftOID ?? ""
                };
            }
        }

        private string CreateHomePagePrompt(AgenteInmobiliarioRequest agente, string descripcionAdicional)
        {
            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine("ERES UN EXPERTO EN DISEÑO WEB Y MARKETING INMOBILIARIO PARA EL MERCADO EN MEXICO.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine($"EN la parte de abajo te estoy dando un ejemplo de como podrias " +
                $" construir el web site. IMPORTANTE quiero que sigas estas instrucciones " +
                $" del dueno de la empreas y que si es necesario cambies todo el ejemplo para " +
                $" disenar tal ocmo se te indico  *** Disena el web page asi: : {descripcionAdicional} **** FIN8**" +
                $"  IMPORTANTE: Dentro de las instrucciones de diseno el agente te esta dando otra informaicon como un url de " +
                $"  imagenes. Incoprora ese url extraelo del texto y ponlo donde el agente de ventas de casa the instruyo" +
                $"  en caso de no instrucicones ponla imagen o imagenes donde crees sea mejor y mas intituivo para el cliente que ocmra casas.");
           
            promptBuilder.AppendLine("Tu rol es diseñar páginas home profesionales y atractivas para agentes de bienes raíces.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("INSTRUCCIONES IMPORTANTES:");
            promptBuilder.AppendLine("✓ Debes ser profesional y honesto en todo momento");
            promptBuilder.AppendLine("✓ NO uses lenguaje ofensivo, mentiras, o groserías");
            promptBuilder.AppendLine("✓ NO inventes información que no te fue proporcionada");
            promptBuilder.AppendLine("✓ Mantén un tono profesional y respetuoso");
            promptBuilder.AppendLine("✓ Crea contenido que inspire confianza y credibilidad");
            promptBuilder.AppendLine("✓ USA LOS DATOS REALES directamente en el HTML, NO uses variables ni placeholders");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("DATOS DEL AGENTE (USA ESTOS DATOS DIRECTAMENTE EN EL HTML):");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine($"Nombre del Equipo/Agente: {agente.NombreEquipoAgente}");
            promptBuilder.AppendLine($"Empresa Broker: {agente.EmpresaBroker}");
            
            if (!string.IsNullOrEmpty(agente.LogoURL))
                promptBuilder.AppendLine($"Logo URL: {agente.LogoURL}");
            
            promptBuilder.AppendLine($"Mensaje Corto: {agente.MensajeCortoClientes}");
            promptBuilder.AppendLine($"Descripción Detallada: {agente.DescripcionDetallada}");
            
            // Contacto - priorizar campos top-level, luego objeto contacto
            string email = !string.IsNullOrEmpty(agente.Email) ? agente.Email : agente.Contacto?.Email ?? "";
            string telefono = !string.IsNullOrEmpty(agente.Telefono) ? agente.Telefono : agente.Contacto?.Telefono ?? "";
            string direccion = !string.IsNullOrEmpty(agente.DireccionFisicaOficina) ? agente.DireccionFisicaOficina : 
                              agente.Contacto?.DireccionOficina ?? agente.DireccionZonaPrincipal ?? "";
            
            if (!string.IsNullOrEmpty(email))
                promptBuilder.AppendLine($"Email: {email}");
            if (!string.IsNullOrEmpty(telefono))
                promptBuilder.AppendLine($"Teléfono: {telefono}");
            if (!string.IsNullOrEmpty(direccion))
                promptBuilder.AppendLine($"Dirección: {direccion}");
            
            // Información adicional del contacto
            if (!string.IsNullOrEmpty(agente.Contacto?.TelefonoSecundario))
                promptBuilder.AppendLine($"Teléfono Secundario: {agente.Contacto.TelefonoSecundario}");
            if (!string.IsNullOrEmpty(agente.Contacto?.SitioWeb))
                promptBuilder.AppendLine($"Sitio Web: {agente.Contacto.SitioWeb}");
            if (!string.IsNullOrEmpty(agente.Contacto?.HorarioAtencion))
                promptBuilder.AppendLine($"Horario de Atención: {agente.Contacto.HorarioAtencion}");

            // Highlights
            if (agente.Highlights != null)
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("DESTACADOS:");
                if (agente.Highlights.VentasRecientes.HasValue)
                    promptBuilder.AppendLine($"  - Ventas Recientes: {agente.Highlights.VentasRecientes}");
                if (agente.Highlights.AnosExperiencia.HasValue)
                    promptBuilder.AppendLine($"  - Años de Experiencia: {agente.Highlights.AnosExperiencia}");
                if (!string.IsNullOrEmpty(agente.Highlights.TicketMediano))
                    promptBuilder.AppendLine($"  - Ticket Mediano: {agente.Highlights.TicketMediano}");
                if (!string.IsNullOrEmpty(agente.Highlights.RangoPreciosMinimo) && !string.IsNullOrEmpty(agente.Highlights.RangoPreciosMaximo))
                    promptBuilder.AppendLine($"  - Rango de Precios: {agente.Highlights.RangoPreciosMinimo} - {agente.Highlights.RangoPreciosMaximo}");
                if (agente.Highlights.CalificacionPromedio.HasValue)
                    promptBuilder.AppendLine($"  - Calificación Promedio: {agente.Highlights.CalificacionPromedio:F1}/5.0");
                if (agente.Highlights.NumeroReviews.HasValue)
                    promptBuilder.AppendLine($"  - Número de Reviews: {agente.Highlights.NumeroReviews}");
                if (!string.IsNullOrEmpty(agente.Highlights.Reconocimientos))
                    promptBuilder.AppendLine($"  - Reconocimientos: {agente.Highlights.Reconocimientos}");
            }

            // Especialidades - solo si tienen datos
            if (agente.Especialidades != null && agente.Especialidades.Count > 0)
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine($"Especialidades: {string.Join(", ", agente.Especialidades)}");
            }

            // Idiomas
            if (agente.Idiomas != null && agente.Idiomas.Count > 0)
            {
                promptBuilder.AppendLine($"Idiomas: {string.Join(", ", agente.Idiomas)}");
            }

            // Premios y Reconocimientos - solo si tienen datos
            if (agente.PremiosReconocimientos != null && agente.PremiosReconocimientos.Count > 0)
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Premios y Reconocimientos:");
                foreach (var premio in agente.PremiosReconocimientos)
                {
                    promptBuilder.AppendLine($"  - {premio}");
                    }
            }

            // Redes sociales
            if (agente.Redes != null)
            {
                var redesConDatos = new List<string>();
                if (!string.IsNullOrEmpty(agente.Redes.Facebook)) redesConDatos.Add($"Facebook: {agente.Redes.Facebook}");
                if (!string.IsNullOrEmpty(agente.Redes.Instagram)) redesConDatos.Add($"Instagram: {agente.Redes.Instagram}");
                if (!string.IsNullOrEmpty(agente.Redes.LinkedIN)) redesConDatos.Add($"LinkedIn: {agente.Redes.LinkedIN}");
                if (!string.IsNullOrEmpty(agente.Redes.Twitter)) redesConDatos.Add($"Twitter: {agente.Redes.Twitter}");
                if (!string.IsNullOrEmpty(agente.Redes.YouTube)) redesConDatos.Add($"YouTube: {agente.Redes.YouTube}");
                if (!string.IsNullOrEmpty(agente.Redes.TikTok)) redesConDatos.Add($"TikTok: {agente.Redes.TikTok}");

                if (redesConDatos.Count > 0)
                {
                    promptBuilder.AppendLine();
                    promptBuilder.AppendLine("Redes Sociales:");
                    foreach (var red in redesConDatos)
                    {
                        promptBuilder.AppendLine($"  - {red}");
                    }
                }
            }

            // Licencias
            if (agente.Licencias != null && agente.Licencias.Count > 0)
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Licencias:");
                foreach (var licencia in agente.Licencias.Where(l => l.Activa))
                {
                    promptBuilder.AppendLine($"  - Licencia #{licencia.NumeroLicencia} ({licencia.Estado})");
                }
            }

            // Descripción adicional del usuario
            if (!string.IsNullOrEmpty(descripcionAdicional))
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("REQUISITOS ADICIONALES DEL CLIENTE:");
                promptBuilder.AppendLine(descripcionAdicional);
            }

            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("EJEMPLO DE ESTRUCTURA (NO USES VARIABLES, USA LOS DATOS REALES):");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine(@"
<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Agente Inmobiliario</title>
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
        }
        .hero {
            min-height: 100vh;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            display: flex;
            align-items: center;
            justify-content: center;
            text-align: center;
            padding: 2rem;
            color: white;
        }
        .hero-content {
            max-width: 800px;
        }
        .logo {
            max-width: 200px;
            margin-bottom: 2rem;
            border-radius: 50%;
        }
        .hero h1 {
            font-size: clamp(2rem, 5vw, 4rem);
            margin-bottom: 1rem;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
        }
        .hero h2 {
            font-size: clamp(1.2rem, 3vw, 2rem);
            margin-bottom: 2rem;
            opacity: 0.9;
        }
        .hero-message {
            font-size: clamp(1rem, 2vw, 1.3rem);
            padding: 2rem;
            background: rgba(255,255,255,0.1);
            backdrop-filter: blur(10px);
            border-radius: 15px;
            margin-bottom: 2rem;
        }
        .contact-info {
            font-size: 1.1rem;
            margin-top: 2rem;
        }
        .section {
            min-height: 100vh;
            padding: 4rem 2rem;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .section:nth-child(even) {
            background-color: #f8f9fa;
        }
        .container {
            max-width: 1200px;
            width: 100%;
        }
        .section-title {
            font-size: clamp(2rem, 4vw, 3rem);
            text-align: center;
            margin-bottom: 3rem;
            color: #2c3e50;
        }
        .grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 2rem;
            margin: 2rem 0;
        }
        .card {
            background: white;
            padding: 2rem;
            border-radius: 15px;
            box-shadow: 0 5px 15px rgba(0,0,0,0.1);
            transition: transform 0.3s;
        }
        .card:hover {
            transform: translateY(-10px);
        }
        .cta {
            min-height: 100vh;
            background: linear-gradient(135deg, #2c3e50 0%, #34495e 100%);
            color: white;
            display: flex;
            align-items: center;
            justify-content: center;
            text-align: center;
            padding: 2rem;
        }
        .btn {
            display: inline-block;
            padding: 1rem 2rem;
            background: #3498db;
            color: white;
            text-decoration: none;
            border-radius: 50px;
            font-size: 1.2rem;
            margin-top: 2rem;
            transition: background 0.3s;
        }
        .btn:hover {
            background: #2980b9;
        }
        @media (max-width: 768px) {
            .grid {
                grid-template-columns: 1fr;
            }
        }
    </style>
</head>
<body>
    <!-- Hero Section - Usa datos reales directamente -->
    <section class='hero'>
        <div class='hero-content'>
            <img src='URL_DEL_LOGO_REAL' alt='Logo' class='logo' />
            <h1>NOMBRE_EQUIPO_REAL</h1>
            <h2>EMPRESA_BROKER_REAL</h2>
            <div class='hero-message'>
                MENSAJE_CORTO_REAL
            </div>
            <div class='contact-info'>
                📧 EMAIL_REAL | 📞 TELEFONO_REAL
            </div>
        </div>
    </section>

    <!-- About Section -->
    <section class='section'>
        <div class='container'>
            <h2 class='section-title'>¿Por Qué Elegirnos?</h2>
            <p style='text-align: center; font-size: 1.2rem; line-height: 1.8;'>
                DESCRIPCION_DETALLADA_REAL
            </p>
        </div>
    </section>

    <!-- Stats Section -->
    <section class='section'>
        <div class='container'>
            <h2 class='section-title'>Nuestros Números</h2>
            <div class='grid'>
                <div class='card'>
                    <h3>✅ Ventas Recientes</h3>
                    <p style='font-size: 2rem; color: #3498db;'>NUMERO_VENTAS</p>
                </div>
                <div class='card'>
                    <h3>⭐ Años de Experiencia</h3>
                    <p style='font-size: 2rem; color: #3498db;'>ANOS_EXPERIENCIA</p>
                </div>
            </div>
        </div>
    </section>

    <!-- CTA Section -->
    <section class='cta'>
        <div>
            <h2 style='font-size: clamp(2rem, 4vw, 3rem); margin-bottom: 1rem;'>
                🏠 ¿Listo para encontrar tu hogar ideal?
            </h2>
            <p style='font-size: 1.3rem; margin-bottom: 2rem;'>
                Contáctanos y comienza tu búsqueda hoy
            </p>
            <a href='mailto:EMAIL_REAL' class='btn'>Agenda una Cita</a>
        </div>
    </section>
</body>
</html>");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("INSTRUCCIONES CRÍTICAS:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("1. CREA UN HTML COMPLETO (con <!DOCTYPE html>, <html>, <head>, <body>, etc.)");
            promptBuilder.AppendLine("2. USA LOS DATOS REALES directamente en el HTML");
            promptBuilder.AppendLine("3. NO uses variables tipo ${agente.nombreEquipoAgente}");
            promptBuilder.AppendLine("4. NO uses placeholders - sustituye con los valores reales proporcionados arriba");
            promptBuilder.AppendLine("5. Ejemplo correcto: <h1>Juan Pérez Team</h1>");
            promptBuilder.AppendLine("6. Ejemplo INCORRECTO: <h1>${agente.nombreEquipoAgente}</h1>");
            promptBuilder.AppendLine("7. La página debe usar 100% del viewport (width: 100vw, height: 100vh para secciones)");
            promptBuilder.AppendLine("8. Diseño RESPONSIVE y profesional para marketing inmobiliario");
            promptBuilder.AppendLine("9. Usa CSS moderno con gradientes, sombras, y animaciones sutiles");
            promptBuilder.AppendLine("10. Incluye botón 'Agenda una Cita' con mailto al email real del agente");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("ESTRUCTURA REQUERIDA:");
            promptBuilder.AppendLine("✓ Hero Section (100vh) con logo, nombre, empresa, mensaje, contacto");
            promptBuilder.AppendLine("✓ About Section (100vh) con descripción detallada");
            promptBuilder.AppendLine("✓ Stats/Highlights Section (100vh) con números destacados");
            promptBuilder.AppendLine("✓ Especialidades Section (si tiene datos)");
            promptBuilder.AppendLine("✓ Premios Section (si tiene datos)");
            promptBuilder.AppendLine("✓ CTA Section (100vh) con llamado a acción y botón 'Agenda una Cita'");
            promptBuilder.AppendLine("✓ Redes sociales (si tiene datos)");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("RESPONDE EN JSON (sin ```json ni ```):");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"htmlCompleto\": \"<!DOCTYPE html><html>...</html>\",");
            promptBuilder.AppendLine("  \"descripcionDiseno\": \"Diseño moderno de página completa con hero section...\",");
            promptBuilder.AppendLine("  \"coloresUsados\": [\"#667eea\", \"#764ba2\", \"#2c3e50\"],");
            promptBuilder.AppendLine("  \"seccionesIncluidas\": [\"hero\", \"about\", \"stats\", \"cta\"]");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("REGLAS FINALES:");
            promptBuilder.AppendLine("✓ HTML completo y funcional listo para renderizar");
            promptBuilder.AppendLine("✓ Todos los estilos CSS inline en <style> dentro de <head>");
            promptBuilder.AppendLine("✓ Responsive design con media queries");
            promptBuilder.AppendLine("✓ Usar DATOS REALES del agente, no variables");
            promptBuilder.AppendLine("✓ Profesional, moderno, y optimizado para marketing");
            promptBuilder.AppendLine("✓ JSON válido sin ``` ni markdown");

            return promptBuilder.ToString();
        }
    }

    /// <summary>
    /// Resultado de la generación de home page
    /// </summary>
    public class RealStateHomePageResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";

        public string TwinID { get; set; } = "";

        public string id { get; set; } = "";

        public string MicrosoftOIDRSA { get; set; } = "";
        public double ProcessingTimeMs { get; set; }

        [JsonProperty("htmlCompleto")]
        public string HtmlCompleto { get; set; } = "";

        [JsonProperty("descripcionDiseno")]
        public string DescripcionDiseno { get; set; } = "";

        [JsonProperty("coloresUsados")]
        public List<string> ColoresUsados { get; set; } = new();

        [JsonProperty("seccionesIncluidas")]
        public List<string> SeccionesIncluidas { get; set; } = new();
    }
}
