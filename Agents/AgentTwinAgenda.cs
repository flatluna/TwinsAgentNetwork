using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente especializado en mejorar mensajes de agenda de visitas a casas
    /// Convierte mensajes simples en emails HTML con diseño atractivo y profesional
    /// </summary>
    public class AgentTwinAgenda
    {
        private readonly ILogger<AgentTwinAgenda> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;

        public AgentTwinAgenda(ILogger<AgentTwinAgenda> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Get Azure OpenAI configuration
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ??
                              configuration["AzureOpenAI:Endpoint"] ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");

                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ??
                            configuration["AzureOpenAI:ApiKey"] ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");

                var deploymentName = "gpt4mini";

                _logger.LogInformation("🔧 Using Azure OpenAI configuration for AgentTwinAgenda:");
                _logger.LogInformation("   • Endpoint: {Endpoint}", endpoint);
                _logger.LogInformation("   • Deployment: {DeploymentName}", deploymentName);
                
                // Initialize Azure OpenAI client
                var clientOptions = new AzureOpenAIClientOptions
                {
                    NetworkTimeout = TimeSpan.FromSeconds(120)
                };
                
                _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), clientOptions);
                _chatClient = _azureClient.GetChatClient(deploymentName);

                _logger.LogInformation("✅ AgentTwinAgenda initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize AgentTwinAgenda");
                throw;
            }
        }

        /// <summary>
        /// Mejora un mensaje de agenda de visita a casa y lo convierte en un email HTML profesional
        /// </summary>
        /// <param name="direccionCasa">Dirección completa de la casa</param>
        /// <param name="direccionExacta">Dirección exacta para el cliente</param>
        /// <param name="puntoPartida">Punto de partida de la visita</param>
        /// <param name="fechaVisita">Fecha de la visita (formato: MM/DD/YYYY)</param>
        /// <param name="horaVisita">Hora de la visita (formato: HH:mm)</param>
        /// <param name="mapaUrl">URL del mapa de Google Maps con direcciones</param>
        /// <param name="nombreCliente">Nombre del cliente (opcional)</param>
        /// <param name="nombreAgente">Nombre del agente inmobiliario (opcional)</param>
        /// <param name="idioma">Idioma del mensaje (default: "es")</param>
        /// <returns>Resultado con el email HTML mejorado</returns>
        public async Task<AgendaEmailResult> MejorarMensajeAgendaAsync(
            string direccionCasa,
            string direccionExacta,
            string puntoPartida,
            string fechaVisita,
            string horaVisita,
            string mapaUrl,
            string nombreCliente = "",
            string nombreAgente = "",
            string idioma = "es")
        {
            _logger.LogInformation("📧 Iniciando mejora de mensaje de agenda");
            _logger.LogInformation("🏠 Casa: {DireccionCasa}", direccionCasa);
            _logger.LogInformation("📅 Fecha: {Fecha} | Hora: {Hora}", fechaVisita, horaVisita);

            var result = new AgendaEmailResult
            {
                Success = false,
                FechaVisita = fechaVisita,
                HoraVisita = horaVisita,
                DireccionCasa = direccionCasa,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                // Crear el mensaje original simple
                string mensajeOriginal = CrearMensajeOriginal(
                    direccionCasa, 
                    direccionExacta, 
                    puntoPartida, 
                    fechaVisita, 
                    horaVisita, 
                    mapaUrl);

                result.MensajeOriginal = mensajeOriginal;

                _logger.LogInformation("🤖 Procesando con Azure OpenAI para mejorar el mensaje...");

                // Llamar a OpenAI para mejorar el mensaje
                var emailMejorado = await MejorarConIA(
                    mensajeOriginal, 
                    direccionCasa,
                    direccionExacta,
                    puntoPartida,
                    fechaVisita, 
                    horaVisita, 
                    mapaUrl,
                    nombreCliente,
                    nombreAgente,
                    idioma);

                if (string.IsNullOrEmpty(emailMejorado))
                {
                    result.ErrorMessage = "No se pudo generar el email mejorado con IA";
                    _logger.LogError("❌ Failed to generate improved email with AI");
                    return result;
                }

                result.EmailHTMLMejorado = emailMejorado;
                result.Success = true;
                
                _logger.LogInformation("✅ Mensaje de agenda mejorado exitosamente");
                _logger.LogInformation("📏 Tamaño del HTML generado: {Size} caracteres", emailMejorado.Length);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error mejorando mensaje de agenda");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Crea el mensaje original simple de agenda
        /// </summary>
        private string CrearMensajeOriginal(
            string direccionCasa,
            string direccionExacta,
            string puntoPartida,
            string fechaVisita,
            string horaVisita,
            string mapaUrl)
        {
            return $@"Hola, tenemos visita para la casa en {direccionCasa} el {fechaVisita} a las {horaVisita}. 
Punto de partida: {puntoPartida}. 
Indicaciones: {direccionExacta}. 
Mapa: {mapaUrl}";
        }

        /// <summary>
        /// Mejora el mensaje usando Azure OpenAI
        /// </summary>
        private async Task<string> MejorarConIA(
            string mensajeOriginal,
            string direccionCasa,
            string direccionExacta,
            string puntoPartida,
            string fechaVisita,
            string horaVisita,
            string mapaUrl,
            string nombreCliente,
            string nombreAgente,
            string idioma)
        {
            try
            {
                // Crear el prompt para OpenAI
                string prompt = CrearPromptMejora(
                    mensajeOriginal,
                    direccionCasa,
                    direccionExacta,
                    puntoPartida,
                    fechaVisita,
                    horaVisita,
                    mapaUrl,
                    nombreCliente,
                    nombreAgente,
                    idioma);

                // Llamar a OpenAI usando el patrón correcto
                var chatMessages = new List<ChatMessage>
                {
                    new SystemChatMessage(CrearInstruccionesAgenda(idioma)),
                    new UserChatMessage(prompt)
                };

                var chatOptions = new ChatCompletionOptions
                {
                    Temperature = 0.7f
                };

                var response = await _chatClient.CompleteChatAsync(chatMessages, chatOptions);
                
                if (response?.Value?.Content == null || response.Value.Content.Count == 0)
                {
                    _logger.LogError("❌ OpenAI returned empty response");
                    return null;
                }

                string aiResponse = response.Value.Content[0].Text;
                _logger.LogInformation("✅ OpenAI response received: {Length} characters", aiResponse.Length);

                // Limpiar respuesta (remover markdown si existe)
                string htmlContent = LimpiarRespuestaHTML(aiResponse);
                
                return htmlContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error mejorando mensaje con IA");
                return null;
            }
        }

        /// <summary>
        /// Crea las instrucciones del sistema para OpenAI
        /// </summary>
        private string CrearInstruccionesAgenda(string idioma)
        {
            return $@"
🎯 IDENTIDAD: Eres un EXPERTO EN COMUNICACIÓN INMOBILIARIA y DISEÑO DE EMAILS PROFESIONALES.

🧠 TU ESPECIALIZACIÓN:
- Experto en crear emails atractivos y profesionales para agentes inmobiliarios
- Especialista en diseño HTML responsive con colores vibrantes y modernos
- Autoridad en comunicación efectiva con clientes de bienes raíces
- Experto en crear experiencias visuales que generen emoción y confianza

📋 REQUISITOS DE RESPUESTA:
1. IDIOMA: Responder en {idioma}
2. FORMATO: HTML puro, sin bloques de código markdown
3. DISEÑO: Usar colores vibrantes, gradientes, iconos y diseño moderno
4. RESPONSIVE: Diseño adaptable a móviles y desktop
5. PROFESIONAL: Mantener tono profesional pero amigable
6. VISUAL: Incluir elementos visuales como iconos, tarjetas, botones

🎨 ELEMENTOS DE DISEÑO A INCLUIR:
- Paleta de colores atractiva (azules, verdes, naranjas vibrantes)
- Gradientes sutiles en headers y secciones
- Iconos para fecha, hora, ubicación, mapa
- Tarjetas con sombras para información clave
- Botones call-to-action prominentes
- Espaciado generoso y tipografía clara
- Secciones bien definidas con colores de fondo

🏠 ESTRUCTURA DEL EMAIL:
1. HEADER: Título principal con gradiente y mensaje de bienvenida
2. RESUMEN: Fecha, hora y dirección en tarjetas visuales
3. DETALLES: Punto de partida e indicaciones claras
4. MAPA: Botón prominente para abrir Google Maps
5. FOOTER: Información de contacto y mensaje de cierre

🎯 IMPORTANTE: 
- Responde SOLO con HTML puro
- NO uses bloques de código markdown (```html)
- El HTML debe ser completo y listo para usar
- Usa estilos inline para compatibilidad con clientes de email
- Haz el diseño visualmente atractivo con colores y elementos modernos";
        }

        /// <summary>
        /// Crea el prompt para mejorar el mensaje
        /// </summary>
        private string CrearPromptMejora(
            string mensajeOriginal,
            string direccionCasa,
            string direccionExacta,
            string puntoPartida,
            string fechaVisita,
            string horaVisita,
            string mapaUrl,
            string nombreCliente,
            string nombreAgente,
            string idioma)
        {
            StringBuilder prompt = new StringBuilder();
            
            prompt.AppendLine($"Mejora el siguiente mensaje de agenda de visita a casa y conviértelo en un email HTML profesional, atractivo y colorido:");
            prompt.AppendLine();
            prompt.AppendLine($"MENSAJE ORIGINAL:");
            prompt.AppendLine(mensajeOriginal);
            prompt.AppendLine();
            prompt.AppendLine($"INFORMACIÓN DETALLADA:");
            prompt.AppendLine($"- Dirección de la casa: {direccionCasa}");
            prompt.AppendLine($"- Dirección exacta: {direccionExacta}");
            prompt.AppendLine($"- Punto de partida: {puntoPartida}");
            prompt.AppendLine($"- Fecha de visita: {fechaVisita}");
            prompt.AppendLine($"- Hora de visita: {horaVisita}");
            prompt.AppendLine($"- URL del mapa: {mapaUrl}");
            
            if (!string.IsNullOrEmpty(nombreCliente))
            {
                prompt.AppendLine($"- Nombre del cliente: {nombreCliente}");
            }
            
            if (!string.IsNullOrEmpty(nombreAgente))
            {
                prompt.AppendLine($"- Nombre del agente: {nombreAgente}");
            }
            
            prompt.AppendLine();
            prompt.AppendLine($"INSTRUCCIONES:");
            prompt.AppendLine($"1. Crea un email HTML completo con diseño moderno y colorido");
            prompt.AppendLine($"2. Usa gradientes, colores vibrantes, iconos y tarjetas visuales");
            prompt.AppendLine($"3. Incluye toda la información de manera clara y atractiva");
            prompt.AppendLine($"4. Agrega un botón prominente para abrir el mapa en Google Maps");
            prompt.AppendLine($"5. Usa estilos inline para compatibilidad");
            prompt.AppendLine($"6. Haz el diseño responsive (móvil y desktop)");
            prompt.AppendLine($"7. Mantén tono profesional pero amigable");
            prompt.AppendLine($"8. Idioma: {idioma}");
            prompt.AppendLine();
            prompt.AppendLine($"Responde SOLO con el código HTML completo, sin markdown:");

            return prompt.ToString();
        }

        /// <summary>
        /// Limpia la respuesta HTML de OpenAI
        /// </summary>
        private string LimpiarRespuestaHTML(string aiResponse)
        {
            // Limpiar respuesta (remover markdown si existe)
            string htmlContent = aiResponse.Trim();
            
            if (htmlContent.StartsWith("```html"))
            {
                htmlContent = htmlContent.Replace("```html", "").Replace("```", "").Trim();
            }
            else if (htmlContent.StartsWith("```"))
            {
                htmlContent = htmlContent.Replace("```", "").Trim();
            }

            return htmlContent;
        }
    }

    #region Data Models

    /// <summary>
    /// Resultado del procesamiento de mensaje de agenda
    /// </summary>
    public class AgendaEmailResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string FechaVisita { get; set; } = string.Empty;
        public string HoraVisita { get; set; } = string.Empty;
        public string DireccionCasa { get; set; } = string.Empty;
        public string MensajeOriginal { get; set; } = string.Empty;
        public string EmailHTMLMejorado { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
    }

    #endregion
}
