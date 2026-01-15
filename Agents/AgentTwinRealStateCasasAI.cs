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
using TwinAgentsNetwork.AzureFunctions;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.Agents
{
    public class FlexibleDoubleConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(double) || objectType == typeof(double?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType == JsonToken.Float || reader.TokenType == JsonToken.Integer)
                return Convert.ToDouble(reader.Value);

            if (reader.TokenType == JsonToken.String)
            {
                var stringValue = reader.Value.ToString();

                if (string.IsNullOrWhiteSpace(stringValue))
                    return objectType == typeof(double?) ? (double?)null : 0.0;

                stringValue = stringValue.Replace(",", "").Trim();

                if (stringValue.Contains("-"))
                {
                    var parts = stringValue.Split('-');
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0].Trim(), out double min) && 
                        double.TryParse(parts[1].Trim(), out double max))
                    {
                        return (min + max) / 2.0;
                    }
                }

                if (double.TryParse(stringValue, out double result))
                    return result;

                return objectType == typeof(double?) ? (double?)null : 0.0;
            }

            return objectType == typeof(double?) ? (double?)null : 0.0;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
                writer.WriteNull();
            else
                writer.WriteValue(value);
        }
    }

    public class FlexibleIntConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(int) || objectType == typeof(int?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType == JsonToken.Integer)
                return Convert.ToInt32(reader.Value);

            if (reader.TokenType == JsonToken.Float)
                return Convert.ToInt32(Math.Round(Convert.ToDouble(reader.Value)));

            if (reader.TokenType == JsonToken.String)
            {
                var stringValue = reader.Value.ToString();

                if (string.IsNullOrWhiteSpace(stringValue))
                    return objectType == typeof(int?) ? (int?)null : 0;

                stringValue = stringValue.Replace(",", "").Trim();

                if (stringValue.Contains("-"))
                {
                    var parts = stringValue.Split('-');
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0].Trim(), out double min) && 
                        double.TryParse(parts[1].Trim(), out double max))
                    {
                        return (int)Math.Round((min + max) / 2.0);
                    }
                }

                if (double.TryParse(stringValue, out double doubleResult))
                    return (int)Math.Round(doubleResult);

                if (int.TryParse(stringValue, out int intResult))
                    return intResult;

                return objectType == typeof(int?) ? (int?)null : 0;
            }

            return objectType == typeof(int?) ? (int?)null : 0;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
                writer.WriteNull();
            else
                writer.WriteValue(value);
        }
    }

    public class AgentTwinRealStateCasasAI
    {
        private readonly ILogger<AgentTwinRealStateCasasAI> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;

        public AgentTwinRealStateCasasAI(ILogger<AgentTwinRealStateCasasAI> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ?? configuration["AzureOpenAI:Endpoint"] ?? "";
                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ?? configuration["AzureOpenAI:ApiKey"] ?? "";
                var deploymentName = "gpt-5-mini";

                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint and API key are required");
                }

                _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _chatClient = _azureClient.GetChatClient(deploymentName);

                _logger.LogInformation("✅ AgentTwinRealStateCasasAI initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize AgentTwinRealStateCasasAI");
                throw;
            }
        }

        public async Task<AnalisisCasaResult> AnalizarCasaParaCompradorAsync(string compradorId, string twinId, string datosTextoCasa)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(compradorId))
                    throw new ArgumentException("Comprador ID is required");

                if (string.IsNullOrEmpty(twinId))
                    throw new ArgumentException("Twin ID is required");

                if (string.IsNullOrEmpty(datosTextoCasa))
                    throw new ArgumentException("Datos de la casa son requeridos");

                _logger.LogInformation("🏠 Analizando casa para comprador: {CompradorId}", compradorId);

                // Limpiar HTML del texto de la casa
                var datosLimpios = RemoveHtmlTags(datosTextoCasa);
                _logger.LogInformation("📄 Datos limpios length: {Length} characters", datosLimpios.Length);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
                var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

                var compradorResult = await compradorCosmosDB.GetCompradorByIdAsync(compradorId, twinId);

                if (!compradorResult.Success || compradorResult.Comprador == null)
                    throw new Exception($"No se pudo obtener el perfil del comprador: {compradorResult.ErrorMessage}");

                var comprador = compradorResult.Comprador;

                _logger.LogInformation("✅ Perfil del comprador obtenido: {Nombre} {Apellido}", comprador.Nombre, comprador.Apellido);

                var analysisPrompt = CreateAnalysisPrompt(comprador, datosLimpios);

                _logger.LogInformation("🤖 Enviando análisis a OpenAI...");

                var message = ChatMessage.CreateUserMessage(analysisPrompt);
                var chatOptions = new ChatCompletionOptions();

                var response = await _chatClient.CompleteChatAsync(new[] { message }, chatOptions);

                if (response?.Value?.Content?.Count == 0)
                    throw new Exception("Empty response from OpenAI");

                var aiResponse = response.Value.Content[0].Text;
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("✅ Análisis completado en {ProcessingTime}ms", processingTime);
                _logger.LogInformation("📄 AI Response preview (first 500 chars): {Preview}", 
                    aiResponse.Length > 500 ? aiResponse.Substring(0, 500) : aiResponse);

                var jsonSettings = new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter> 
                    { 
                        new FlexibleDoubleConverter(),
                        new FlexibleIntConverter()
                    },
                    NullValueHandling = NullValueHandling.Ignore
                };

                var analysisResult = JsonConvert.DeserializeObject<AnalisisCasaResult>(aiResponse, jsonSettings);
                analysisResult.DatosCasa.EstatusCasa = "";
                if (analysisResult != null)
                {
                    analysisResult.Success = true;
                    analysisResult.ProcessingTimeMs = processingTime;
                    analysisResult.CompradorId = compradorId;
                    analysisResult.TwinId = twinId;

                    // Convertir el HTML de la carta a texto plano
                    if (!string.IsNullOrEmpty(analysisResult.DatosCasa.CartaClienteHTML))
                    {
                        analysisResult.DatosCasa.CartaCliente = RemoveHtmlTags(analysisResult.DatosCasa.CartaClienteHTML);
                        _logger.LogInformation("✅ Carta convertida a texto plano: {Length} caracteres", analysisResult.DatosCasa.CartaCliente.Length);
                    }
                }

                return analysisResult ?? new AnalisisCasaResult
                {
                    Success = false,
                    ErrorMessage = "Failed to parse OpenAI response"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analizando casa");
                return new AnalisisCasaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                };
            }
        }
        public async Task<AnalisisCasaResult> AnalizarEditCasaParaCompradorAsync(string compradorId, 
            string twinId, AnalyzarEditCasaRequest editCasaData)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(compradorId))
                    throw new ArgumentException("Comprador ID is required");

                if (string.IsNullOrEmpty(twinId))
                    throw new ArgumentException("Twin ID is required");

                if (editCasaData == null)
                    throw new ArgumentException("Datos de la casa son requeridos");

                _logger.LogInformation("🏠 Editando casa para comprador: {CompradorId}", compradorId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
                var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

                var compradorResult = await compradorCosmosDB.GetCompradorByIdAsync(compradorId, twinId);

                if (!compradorResult.Success || compradorResult.Comprador == null)
                    throw new Exception($"No se pudo obtener el perfil del comprador: {compradorResult.ErrorMessage}");

                var comprador = compradorResult.Comprador;

                _logger.LogInformation("✅ Perfil del comprador obtenido: {Nombre} {Apellido}", comprador.Nombre, comprador.Apellido);

                var editPrompt = CreateEditAnalysisPrompt(comprador, editCasaData);

                _logger.LogInformation("🤖 Enviando análisis de edición a OpenAI...");
                editPrompt = editPrompt + "" +
                    " Asegurate de seguir estas isntrucciones extras del cliente que vende casas: "
                    + editCasaData.Prompt;
                 
                var message = ChatMessage.CreateUserMessage(editPrompt);
                var chatOptions = new ChatCompletionOptions();

                var response = await _chatClient.CompleteChatAsync(new[] { message }, chatOptions);

                if (response?.Value?.Content?.Count == 0)
                    throw new Exception("Empty response from OpenAI");

                var aiResponse = response.Value.Content[0].Text;
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("✅ Análisis de edición completado en {ProcessingTime}ms", processingTime);
                _logger.LogInformation("📄 AI Response preview (first 500 chars): {Preview}",
                    aiResponse.Length > 500 ? aiResponse.Substring(0, 500) : aiResponse);

                var jsonSettings = new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter>
                    {
                        new FlexibleDoubleConverter(),
                        new FlexibleIntConverter()
                    },
                    NullValueHandling = NullValueHandling.Ignore
                };

                var analysisResult = JsonConvert.DeserializeObject<AnalisisCasaResult>(aiResponse, jsonSettings);
                
                if (analysisResult != null)
                {
                    analysisResult.Success = true;
                    analysisResult.ProcessingTimeMs = processingTime;
                    analysisResult.CompradorId = compradorId;
                    analysisResult.TwinId = twinId;
                    
                    // Preserve the original IDs and status from edit request
                    analysisResult.DatosCasa.id = editCasaData.Id;
                    analysisResult.DatosCasa.TwinID = editCasaData.TwinID;
                    analysisResult.DatosCasa.ClienteID = editCasaData.ClienteID;
                    analysisResult.DatosCasa.EstatusCasa = editCasaData.EstatusCasa;

                    // Convertir el HTML de la carta a texto plano
                    if (!string.IsNullOrEmpty(analysisResult.DatosCasa.CartaClienteHTML))
                    {
                        analysisResult.DatosCasa.CartaCliente = RemoveHtmlTags(analysisResult.DatosCasa.CartaClienteHTML);
                        _logger.LogInformation("✅ Carta convertida a texto plano: {Length} caracteres", analysisResult.DatosCasa.CartaCliente.Length);
                    }
                }

                return analysisResult ?? new AnalisisCasaResult
                {
                    Success = false,
                    ErrorMessage = "Failed to parse OpenAI response"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error editando casa");
                return new AnalisisCasaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                };
            }
        }

        private string RemoveHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            // Primero eliminar el contenido completo de <style> y <script>
            var styleRegex = new System.Text.RegularExpressions.Regex(@"<style[^>]*>.*?</style>", 
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string plainText = styleRegex.Replace(html, "");

            var scriptRegex = new System.Text.RegularExpressions.Regex(@"<script[^>]*>.*?</script>", 
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            plainText = scriptRegex.Replace(plainText, "");

            // Eliminar etiquetas <head> completas
            var headRegex = new System.Text.RegularExpressions.Regex(@"<head[^>]*>.*?</head>", 
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            plainText = headRegex.Replace(plainText, "");

            // Reemplazar <br>, <br/>, <br /> con salto de línea
            var brRegex = new System.Text.RegularExpressions.Regex(@"<br\s*/?>", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            plainText = brRegex.Replace(plainText, "\n");

            // Reemplazar </p>, </div>, </h1>, </h2>, etc con salto de línea doble
            var blockRegex = new System.Text.RegularExpressions.Regex(@"</(p|div|h[1-6]|li|tr)>", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            plainText = blockRegex.Replace(plainText, "\n\n");

            // Eliminar todas las demás etiquetas HTML
            var tagRegex = new System.Text.RegularExpressions.Regex(@"<[^>]+>");
            plainText = tagRegex.Replace(plainText, "");

            // Decodificar entidades HTML
            plainText = System.Net.WebUtility.HtmlDecode(plainText);

            // Limpiar saltos de línea múltiples (más de 2 consecutivos)
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\n{3,}", "\n\n");

            // Limpiar espacios en blanco al inicio de cada línea
            var lines = plainText.Split('\n');
            lines = lines.Select(line => line.Trim()).ToArray();
            plainText = string.Join("\n", lines);

            // Eliminar líneas vacías al principio y al final
            plainText = plainText.Trim();

            return plainText;
        }

        private string CreateAnalysisPrompt(CompradorRequest comprador, string datosTextoCasa)
        {
            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine("ERES UN EXPERTO ANALISTA INMOBILIARIO Y AGENTE DE VENTAS PROFESIONAL.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("INFORMACIÓN DEL CLIENTE:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine($"Nombre: {comprador.Nombre} {comprador.Apellido}");
            promptBuilder.AppendLine($"Email: {comprador.Email}");
            promptBuilder.AppendLine($"Teléfono: {comprador.Telefono}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("DATOS DE LA CASA:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine(datosTextoCasa);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("TAREAS:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("1. EXTRAE todos los datos estructurados (precio, ubicación, características, amenidades)");
            promptBuilder.AppendLine("2. CREA una CARTA/EMAIL profesional en HTML dirigida al cliente (campo: cartaClienteHTML)");
            promptBuilder.AppendLine("3. IDENTIFICA la direccion es importante de la unicacion de la casa no las coordenadas solo direccion");
            promptBuilder.AppendLine("4. NO generes cartaCliente (se creará automáticamente desde el HTML)");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("LA CARTA HTML DEBE:");
            promptBuilder.AppendLine("• Estar dirigida personalmente al cliente (usar su nombre)");
            promptBuilder.AppendLine("• Explicar que esta es una de las propiedades seleccionadas para él/ella");
            promptBuilder.AppendLine("• Incluir TODOS los detalles de la casa de forma atractiva y profesional");
            promptBuilder.AppendLine("• Usar HTML moderno con CSS inline (colores, grids, tablas, cards)");
            promptBuilder.AppendLine("• Tener estructura profesional de email inmobiliario");
            promptBuilder.AppendLine("• SER INFORMATIVA, NO incluir llamados a la acción ni invitaciones a agendar");
            promptBuilder.AppendLine("• Incluir:");
            promptBuilder.AppendLine("  - Saludo personalizado");
            promptBuilder.AppendLine("  - Introducción explicando por qué se seleccionó esta casa");
            promptBuilder.AppendLine("  - Título atractivo de la propiedad");
            promptBuilder.AppendLine("  - Descripción detallada con formato visual");
            promptBuilder.AppendLine("  - Tabla o grid con características principales");
            promptBuilder.AppendLine("  - Sección de distribución por niveles");
            promptBuilder.AppendLine("  - Amenidades destacadas con íconos/emojis");
            promptBuilder.AppendLine("  - Ubicación y mapa");
            promptBuilder.AppendLine("  - Despedida profesional simple");
            promptBuilder.AppendLine("  - NO incluir botones de 'Agendar visita' ni llamados a acción");
            promptBuilder.AppendLine("  - NO invitar al cliente a responder o tomar acciones");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("COLORES SUGERIDOS PARA HTML:");
            promptBuilder.AppendLine("• Encabezados: #2C3E50 (azul oscuro)");
            promptBuilder.AppendLine("• Acentos: #3498DB (azul)");
            promptBuilder.AppendLine("• Precio: #27AE60 (verde)");
            promptBuilder.AppendLine("• Fondo: #F8F9FA (gris claro)");
            promptBuilder.AppendLine("• Bordes: #DEE2E6 (gris medio)");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("RESPONDE EN JSON (sin ```json ni ```):");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"datosCasaExtraidos\": {");
            promptBuilder.AppendLine("    \"precio\": 4190000,");
            promptBuilder.AppendLine("    \"moneda\": \"MXN\",");
            promptBuilder.AppendLine("    \"mantenimiento\": null,");
            promptBuilder.AppendLine("    \"ciudad\": \"Querétaro\",");
            promptBuilder.AppendLine("    \"estado\": \"Querétaro\",");
            promptBuilder.AppendLine("    \"colonia\": \"Juriquilla\",");
            promptBuilder.AppendLine("    \"fraccionamiento\": \"Punta Juriquilla\",");
            promptBuilder.AppendLine("    \"condominio\": \"\",");
            promptBuilder.AppendLine("    \"metrosLote\": 180,");
            promptBuilder.AppendLine("    \"metrosConstruccion\": 231,");
            promptBuilder.AppendLine("    \"metrosCobertura\": null,");
            promptBuilder.AppendLine("    \"recamaras\": 4,");
            promptBuilder.AppendLine("    \"banos\": 3,");
            promptBuilder.AppendLine("    \"medioBanos\": 0,");
            promptBuilder.AppendLine("    \"estacionamientos\": 3,");
            promptBuilder.AppendLine("    \"antiguedad\": 6,");
            promptBuilder.AppendLine("    \"niveles\": 2,");
            promptBuilder.AppendLine("    \"estadoConservacion\": \"Excelente\",");
            promptBuilder.AppendLine("    \"distribucion\": \"Planta Baja: Estancia abierta con sala-comedor integrados al jardín, cocina moderna con barra desayunador, recámara/estudio con baño completo, jardín en escuadra, estacionamiento para 3 autos con bodega. Planta Alta: Recámara principal con walk-in closet y baño completo con doble lavabo, dos recámaras secundarias con clóset compartiendo baño completo, sala de TV/family room.\",");
            promptBuilder.AppendLine("    \"amenidadesCondominio\": [],");
            promptBuilder.AppendLine("    \"amenidadesFraccionamiento\": [\"escuelas cercanas\", \"pet friendly\"],");
            promptBuilder.AppendLine("    \"amenidadesCasa\": [\"cocina integral\", \"jardín\", \"bodega\", \"walk-in closet\", \"canceles pared a pared\", \"área de lavado independiente\"],");
            promptBuilder.AppendLine("    \"cartaClienteHTML\": \"<html><head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333;max-width:800px;margin:0 auto;padding:20px;background-color:#F8F9FA}.header{background:#2C3E50;color:white;padding:20px;text-align:center;border-radius:8px 8px 0 0}.precio{color:#27AE60;font-size:28px;font-weight:bold}.section{background:white;padding:20px;margin:15px 0;border-radius:8px;box-shadow:0 2px 4px rgba(0,0,0,0.1)}.caracteristicas{display:grid;grid-template-columns:repeat(3,1fr);gap:15px;margin:20px 0}.card{background:#F8F9FA;padding:15px;border-left:4px solid #3498DB;border-radius:4px}h2{color:#2C3E50;border-bottom:2px solid #3498DB;padding-bottom:10px}.amenidad{display:inline-block;background:#E3F2FD;color:#1976D2;padding:8px 15px;margin:5px;border-radius:20px;font-size:14px}.footer{text-align:center;color:#666;padding:20px;border-top:2px solid #DEE2E6}</style></head><body><div class='header'><h1>🏠 Propiedad Seleccionada Para Usted</h1></div><div class='section'><p>Estimado/a Juan Pérez,</p><p>Es un placer presentarle esta excepcional propiedad que hemos seleccionado especialmente para usted.</p></div><div class='section><h2>Vive Momentos Inolvidables en Juriquilla</h2><div class='precio'>$4,190,000 MXN</div><p>Esta hermosa casa moderna y funcional destaca por su diseño de espacios abiertos y alta luminosidad...</p></div><div class='caracteristicas'><div class='card'><strong>🛏️ Recámaras</strong><br>4</div><div class='card'><strong>🚿 Baños</strong><br>3</div><div class='card'><strong>🚗 Estacionamientos</strong><br>3</div><div class='card'><strong>📐 M² Construcción</strong><br>231 m²</div><div class='card'><strong>📏 M² Terreno</strong><br>180 m²</div><div class='card'><strong>📅 Antigüedad</strong><br>6 años</div></div><div class='section'><h2>Distribución</h2><p><strong>Planta Baja:</strong> Estancia abierta...</p><p><strong>Planta Alta:</strong> Recámara principal...</p></div><div class='section'><h2>Amenidades</h2><span class='amenidad'>🍳 Cocina integral</span><span class='amenidad'>🌳 Jardín</span></div><div class='section'><h2>Ubicación</h2><p>📍 Punta Juriquilla, Juriquilla, Querétaro</p><p><a href='https://www.google.com/maps/search/?api=1&query=20.6319,-100.4465'>Ver en Google Maps</a></p></div><div class='footer'><p>Saludos cordiales</p></div></body></html>\",");
            promptBuilder.AppendLine("    \"cercania\": [],");
            promptBuilder.AppendLine("    \"direccionCompleta\": \"Punta Juriquilla, Juriquilla, Querétaro, Querétaro, México\",");
            promptBuilder.AppendLine("    \"urlGoogleMaps\": \"https://www.google.com/maps/search/?api=1&query=20.6319,-100.4465\"");
            promptBuilder.AppendLine("  }");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("REGLAS CRÍTICAS:");
            promptBuilder.AppendLine("✓ Usa NÚMEROS para cantidades (precio: 4190000, no \"4,190,000\")");
            promptBuilder.AppendLine("✓ cartaClienteHTML: HTML completo con CSS inline, profesional, colorido, con grids/tablas");
            promptBuilder.AppendLine("✓ NO incluyas cartaCliente en el JSON (se generará automáticamente desde el HTML)");
            promptBuilder.AppendLine("✓ Personaliza el saludo con el nombre del cliente: {comprador.Nombre} {comprador.Apellido}");
            promptBuilder.AppendLine("✓ distribucion: POR NIVEL (Planta Baja:..., Planta Alta:...)");
            promptBuilder.AppendLine("✓ direccionCompleta: Formato completo");
            promptBuilder.AppendLine("✓ Coordenadas estimadas según ubicación real");
            promptBuilder.AppendLine("✓ JSON SIN ``` ni markdown");
            promptBuilder.AppendLine("✓ NO incluir botones ni links de 'Agendar visita' o 'Contactar'");
            promptBuilder.AppendLine("✓ NO invitar al cliente a tomar acciones (el UI manejará eso)");
            promptBuilder.AppendLine("✓ La carta debe ser SOLO informativa sobre la propiedad");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("ESTRUCTURA DE LA CARTA HTML:");
            promptBuilder.AppendLine("1. Header con título atractivo");
            promptBuilder.AppendLine("2. Saludo personalizado al cliente");
            promptBuilder.AppendLine("3. Introducción explicando la selección");
            promptBuilder.AppendLine("4. Título de la propiedad + precio destacado");
            promptBuilder.AppendLine("5. Descripción atractiva");
            promptBuilder.AppendLine("6. Grid/tabla con características (recámaras, baños, m², etc.)");
            promptBuilder.AppendLine("7. Sección de distribución por niveles");
            promptBuilder.AppendLine("8. Amenidades con badges/pills coloridos");
            promptBuilder.AppendLine("9. Ubicación con link a Google Maps");
            promptBuilder.AppendLine("10. Despedida simple y profesional");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("IMPORTANTE:");
            promptBuilder.AppendLine("• La carta es INFORMATIVA, no persuasiva");
            promptBuilder.AppendLine("• NO incluir frases como 'Agende su visita', 'Contáctenos', 'Responda este email'");
            promptBuilder.AppendLine("• NO incluir botones de acción (el UI tendrá sus propios botones)");
            promptBuilder.AppendLine("• Despedida simple: 'Saludos cordiales' o similar, sin invitaciones");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("NOTA: El sistema convertirá automáticamente el HTML a texto plano para el campo cartaCliente. Solo genera cartaClienteHTML.");

            return promptBuilder.ToString();
        }
        private string CreateEditAnalysisPrompt(CompradorRequest comprador, AnalyzarEditCasaRequest editCasaData)
        {
            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine("ERES UN EXPERTO AGENTE INMOBILIARIO ESPECIALIZADO EN CREAR EMAILS PROFESIONALES Y ATRACTIVOS.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("INFORMACIÓN DEL PROSPECTO:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine($"Nombre: {comprador.Nombre} {comprador.Apellido}");
            promptBuilder.AppendLine($"Email: {comprador.Email}");
            promptBuilder.AppendLine($"Teléfono: {comprador.Telefono}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("DATOS DE LA PROPIEDAD EDITADA:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine($"Precio: ${editCasaData.Precio:N0} {editCasaData.Moneda}");
            if (editCasaData.Recamaras.HasValue)
                promptBuilder.AppendLine($"Recámaras: {editCasaData.Recamaras}");
            if (editCasaData.Banos.HasValue)
                promptBuilder.AppendLine($"Baños: {editCasaData.Banos}");
            if (editCasaData.MedioBanos.HasValue && editCasaData.MedioBanos > 0)
                promptBuilder.AppendLine($"Medios Baños: {editCasaData.MedioBanos}");
            if (editCasaData.Estacionamientos.HasValue)
                promptBuilder.AppendLine($"Estacionamientos: {editCasaData.Estacionamientos}");
            if (editCasaData.MetrosConstruccion.HasValue)
                promptBuilder.AppendLine($"Metros Construcción: {editCasaData.MetrosConstruccion} m²");
            if (editCasaData.MetrosLote.HasValue)
                promptBuilder.AppendLine($"Metros Terreno: {editCasaData.MetrosLote} m²");
            if (editCasaData.Antiguedad.HasValue)
                promptBuilder.AppendLine($"Antigüedad: {editCasaData.Antiguedad} años");
            if (!string.IsNullOrEmpty(editCasaData.DireccionCompleta))
                promptBuilder.AppendLine($"Dirección: {editCasaData.DireccionCompleta}");
            if (!string.IsNullOrEmpty(editCasaData.Ciudad))
                promptBuilder.AppendLine($"Ciudad: {editCasaData.Ciudad}");
            if (!string.IsNullOrEmpty(editCasaData.Estado))
                promptBuilder.AppendLine($"Estado: {editCasaData.Estado}");
            if (!string.IsNullOrEmpty(editCasaData.Colonia))
                promptBuilder.AppendLine($"Colonia: {editCasaData.Colonia}");
            if (!string.IsNullOrEmpty(editCasaData.Fraccionamiento))
                promptBuilder.AppendLine($"Fraccionamiento: {editCasaData.Fraccionamiento}");
            if (!string.IsNullOrEmpty(editCasaData.Distribucion))
                promptBuilder.AppendLine($"Distribución: {editCasaData.Distribucion}");
            if (!string.IsNullOrEmpty(editCasaData.DescripcionExtra))
                promptBuilder.AppendLine($"Descripción Adicional: {editCasaData.DescripcionExtra}");
            if (editCasaData.AmenidadesCasa?.Count > 0)
                promptBuilder.AppendLine($"Amenidades Casa: {string.Join(", ", editCasaData.AmenidadesCasa)}");
            if (editCasaData.AmenidadesFraccionamiento?.Count > 0)
                promptBuilder.AppendLine($"Amenidades Fraccionamiento: {string.Join(", ", editCasaData.AmenidadesFraccionamiento)}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("TU TAREA:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("Genera un EMAIL PROFESIONAL EN HTML dirigido al prospecto con la información de la propiedad.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("ESTRUCTURA DEL EMAIL (cartaClienteHTML):");
            promptBuilder.AppendLine("1. Header profesional con título: \"🏠 Propiedad Seleccionada Para Usted\"");
            promptBuilder.AppendLine("2. Saludo personalizado: \"Estimado/a {comprador.Nombre} {comprador.Apellido},\"");
            promptBuilder.AppendLine("3. Mensaje introductorio cordial explicando que es una propiedad seleccionada");
            promptBuilder.AppendLine("4. Título atractivo de la propiedad basado en ubicación/características");
            promptBuilder.AppendLine("5. Precio destacado en grande y verde: ${editCasaData.Precio:N0} {editCasaData.Moneda}");
            promptBuilder.AppendLine("6. Descripción breve pero atractiva de la propiedad");
            promptBuilder.AppendLine("7. Grid/Cards con características principales (recámaras, baños, m², estacionamientos, antigüedad)");
            promptBuilder.AppendLine("8. Distribución detallada por niveles si está disponible");
            promptBuilder.AppendLine("9. Amenidades con emojis/iconos bonitos");
            promptBuilder.AppendLine("10. Ubicación con dirección completa");
            promptBuilder.AppendLine("11. Link a Google Maps (generar URL apropiada)");
            promptBuilder.AppendLine("12. Despedida profesional: \"Saludos cordiales\"");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("DISEÑO Y COLORES:");
            promptBuilder.AppendLine("• Usa CSS inline con estilos modernos y profesionales");
            promptBuilder.AppendLine("• Encabezados: #2C3E50 (azul oscuro)");
            promptBuilder.AppendLine("• Acentos y bordes: #3498DB (azul claro)");
            promptBuilder.AppendLine("• Precio destacado: #27AE60 (verde) en tamaño grande");
            promptBuilder.AppendLine("• Fondo general: #F8F9FA (gris muy claro)");
            promptBuilder.AppendLine("• Cards/secciones: fondo blanco con sombras suaves");
            promptBuilder.AppendLine("• Amenidades: badges con fondo #E3F2FD y texto #1976D2");
            promptBuilder.AppendLine("• Grid de características: 2-3 columnas responsivo");
            promptBuilder.AppendLine("• Padding y spacing generosos para mejor lectura");
            promptBuilder.AppendLine("• IMPORTANTE: Sigue estas instrucciones adicionales del agente de ventas de casas e incluyelo en tu mensaje al cliente" +
                " asegurate de no poner palabras obsenas, racistas o que ofendan al cliente;" +
                " estas son las instrucciones adicionales : " + editCasaData.Prompt);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("EJEMPLO DE ESTRUCTURA ESPERADA:");
            promptBuilder.AppendLine(@" Esto es solo un ejemplo y esta incompleto pon todos los datos que tengas disponibles
<html>
<head>
<style>
body{font-family:Arial,sans-serif;line-height:1.6;color:#333;max-width:800px;margin:0 auto;padding:20px;background-color:#F8F9FA}
.header{background:#2C3E50;color:white;padding:20px;text-align:center;border-radius:8px 8px 0 0}
.precio{color:#27AE60;font-size:28px;font-weight:bold}
.section{background:white;padding:20px;margin:15px 0;border-radius:8px;box-shadow:0 2px 4px rgba(0,0,0,0.1)}
.caracteristicas{display:grid;grid-template-columns:repeat(3,1fr);gap:15px;margin:20px 0}
.card{background:#F8F9FA;padding:15px;border-left:4px solid #3498DB;border-radius:4px}
h2{color:#2C3E50;border-bottom:2px solid #3498DB;padding-bottom:10px}
.amenidad{display:inline-block;background:#E3F2FD;color:#1976D2;padding:8px 15px;margin:5px;border-radius:20px;font-size:14px}
</style>
</head>
<body>
<div class='header'><h1>🏠 Propiedad Seleccionada Para Usted</h1></div>
<div class='section'>
<p>Estimado/a {comprador.Nombre} {comprador.Apellido},</p>
<p>Es un placer presentarle esta excepcional propiedad que hemos seleccionado especialmente para usted.</p>
</div>
<div class='section'>
<h2>[Título Atractivo]</h2>
<div class='precio'>${editCasaData.Precio:N0} {editCasaData.Moneda}</div>
<p>[Descripción atractiva]</p>
</div>
<div class='caracteristicas'>
<div class='card'><strong>🛏️ Recámaras</strong><br>{editCasaData.Recamaras}</div>
<!-- más cards -->
</div>
<!-- más secciones -->
<div class='section'>
<p>Saludos cordiales</p>
</div>
</body>
</html>");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("RESPONDE EN JSON (sin ```json ni ```):");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"datosCasaExtraidos\": {");
            promptBuilder.AppendLine($"    \"precio\": {editCasaData.Precio},");
            promptBuilder.AppendLine($"    \"moneda\": \"{editCasaData.Moneda}\",");
            promptBuilder.AppendLine($"    \"recamaras\": {editCasaData.Recamaras ?? 0},");
            promptBuilder.AppendLine($"    \"banos\": {editCasaData.Banos ?? 0},");
            promptBuilder.AppendLine($"    \"medioBanos\": {editCasaData.MedioBanos ?? 0},");
            promptBuilder.AppendLine($"    \"estacionamientos\": {editCasaData.Estacionamientos ?? 0},");
            promptBuilder.AppendLine($"    \"metrosConstruccion\": {editCasaData.MetrosConstruccion ?? 0},");
            promptBuilder.AppendLine($"    \"metrosLote\": {editCasaData.MetrosLote ?? 0},");
            promptBuilder.AppendLine($"    \"antiguedad\": {editCasaData.Antiguedad ?? 0},");
            promptBuilder.AppendLine($"    \"ciudad\": \"{editCasaData.Ciudad}\",");
            promptBuilder.AppendLine($"    \"estado\": \"{editCasaData.Estado}\",");
            promptBuilder.AppendLine($"    \"colonia\": \"{editCasaData.Colonia}\",");
            promptBuilder.AppendLine($"    \"direccionCompleta\": \"{editCasaData.DireccionCompleta}\",");
            promptBuilder.AppendLine("    \"distribucion\": \"[Distribucion por niveles si aplica]\",");
            promptBuilder.AppendLine("    \"amenidadesCasa\": [...],");
            promptBuilder.AppendLine("    \"cartaClienteHTML\": \"[HTML COMPLETO DEL EMAIL]\",");
            promptBuilder.AppendLine("    \"urlGoogleMaps\": \"[URL de Google Maps]\"");
            promptBuilder.AppendLine("  }");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("REGLAS CRÍTICAS:");
            promptBuilder.AppendLine("✓ USA NÚMEROS sin formateo para cantidades (precio: 4190000, no \"4,190,000\")");
            promptBuilder.AppendLine("✓ cartaClienteHTML: HTML COMPLETO, profesional, colorido, con diseño moderno");
            promptBuilder.AppendLine("✓ Personaliza SIEMPRE con nombre del prospecto");
            promptBuilder.AppendLine("✓ Email en ESPAÑOL, tono profesional y cordial");
            promptBuilder.AppendLine("✓ NO incluir botones de \"Agendar\" o llamados a acción agresivos");
            promptBuilder.AppendLine("✓ Despedida simple: \"Saludos cordiales\" sin más invitaciones");
            promptBuilder.AppendLine("✓ JSON VÁLIDO sin ``` ni markdown");
            promptBuilder.AppendLine("✓ Incluye TODOS los datos proporcionados en el email");
            promptBuilder.AppendLine("✓ Email debe ser INFORMATIVO, no de venta agresiva");
            promptBuilder.AppendLine("✓ Genera URL de Google Maps basada en la dirección");

            return promptBuilder.ToString();
        }
    }

    public class AnalisisCasaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public double ProcessingTimeMs { get; set; }
        public string CompradorId { get; set; } = "";
        public string TwinId { get; set; } = "";

        [JsonProperty("datosCasaExtraidos")]
        public DatosCasaExtraidos DatosCasa { get; set; } = new();
    }

    public class DatosCasaExtraidos
    {
        [JsonProperty("id")]
        public string id { get; set; } = "";

        [JsonProperty("microsoftOID")]
        public string MicrosoftOID { get; set; } = "";


        [JsonProperty("casaProspectoID")]
        public string CasaProspectoID { get; set; } = "";

        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = ""; 

        [JsonProperty("precio")]
        public double Precio { get; set; }


        [JsonProperty("estatusCasa")]
        public string EstatusCasa { get; set; }

        [JsonProperty("clienteID")]
        public string ClienteID { get; set; } = "";
        [JsonProperty("moneda")]
        public string Moneda { get; set; } = "MXN";

        [JsonProperty("mantenimiento")]
        public double? Mantenimiento { get; set; }

        [JsonProperty("ciudad")]
        public string Ciudad { get; set; } = "";

        [JsonProperty("estado")]
        public string Estado { get; set; } = "";

        [JsonProperty("colonia")]
        public string Colonia { get; set; } = "";

        [JsonProperty("fraccionamiento")]
        public string Fraccionamiento { get; set; } = "";

        [JsonProperty("condominio")]
        public string Condominio { get; set; } = "";

        [JsonProperty("metrosLote")]
        public double? MetrosLote { get; set; }

        [JsonProperty("metrosConstruccion")]
        public double? MetrosConstruccion { get; set; }

        [JsonProperty("metrosCobertura")]
        public double? MetrosCobertura { get; set; }

        [JsonProperty("recamaras")]
        public int? Recamaras { get; set; }

        [JsonProperty("banos")]
        public int? Banos { get; set; }

        [JsonProperty("medioBanos")]
        public int? MedioBanos { get; set; }

        [JsonProperty("estacionamientos")]
        public int? Estacionamientos { get; set; }

        [JsonProperty("antiguedad")]
        public int? Antiguedad { get; set; }

        [JsonProperty("niveles")]
        public int? Niveles { get; set; }

        [JsonProperty("estadoConservacion")]
        public string EstadoConservacion { get; set; } = "";

        [JsonProperty("distribucion")]
        public string Distribucion { get; set; } = "";

        [JsonProperty("amenidadesCondominio")]
        public List<string> AmenidadesCondominio { get; set; } = new();

        [JsonProperty("amenidadesFraccionamiento")]
        public List<string> AmenidadesFraccionamiento { get; set; } = new();

        [JsonProperty("amenidadesCasa")]
        public List<string> AmenidadesCasa { get; set; } = new();

        [JsonProperty("cartaClienteHTML")]
        public string CartaClienteHTML { get; set; } = "";

        [JsonProperty("cartaCliente")]
        public string CartaCliente { get; set; } = "";

        [JsonProperty("cercania")]
        public List<string> Cercania { get; set; } = new();

        [JsonProperty("direccionCompleta")]
        public string DireccionCompleta { get; set; } = "";

        [JsonProperty("coordenadas")]
        public Coordenadas Coordenadas { get; set; } = new();

        [JsonProperty("urlGoogleMaps")]
        public string UrlGoogleMaps { get; set; } = "";

        [JsonProperty("emailEditedHTML")]
        public string EmailEditedHTML { get; set; } = "";

        [JsonProperty("agendarCasa")]
        public Agenda AgendarCasa { get; set; } = new();


        [JsonProperty("fechaCreacion")]
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;


        [JsonProperty("casaURL")]
        public string CasaURL { get; set; } = string.Empty;


        [JsonProperty("urlPropiedad")]
        public string URLPropiedad { get; set; } = string.Empty;

         
    }

    public class Agenda
    {
        [JsonProperty("direccionCasa")]
        public string DireccionCasa { get; set; } = "";

        [JsonProperty("direccionPartida")]
        public string DireccionPartida { get; set; } = "";

        [JsonProperty("direccionExactaCasa")]
        public string DireccionExactaCasa { get; set; } = "";

        [JsonProperty("fechaVisita")]
        public string FechaVisita { get; set; } = "";

        [JsonProperty("horaVisita")]
        public string HoraVisita { get; set; } = "";

        [JsonProperty("MensajeCliente")]
        public string MensajeCliente { get; set; } = "";
        [JsonProperty("LinkDirecciones")]
        public string LinkDirecciones { get; set; } = "";
        [JsonProperty("ComentariosrealStateEgent")]
        public string ComentariosrealStateEgent { get; set; } = "";

        [JsonProperty("ComentariosClienteSiGusto")]
        public string ComentariosClienteSiGusto { get; set; } = "";

        [JsonProperty("ComentariosClienteNoGusto")]
        public string ComentariosClienteNoGusto { get; set; } = "";

        [JsonProperty("ScoreAgente")]
        public int ScoreAgente { get; set; }
        [JsonProperty("ScoreCliente")]
        public int ScoreCliente { get; set; }


        [JsonProperty("fechaRealVisita")]
        public string FechaRealVisita { get; set; } = "";

        [JsonProperty("horaRealVisita")]
        public string HoraRealVisita { get; set; } = "";




    }

    public class Coordenadas
    {
        [JsonProperty("latitud")]
        public double? Latitud { get; set; }

        [JsonProperty("longitud")]
        public double? Longitud { get; set; }
    }
}
