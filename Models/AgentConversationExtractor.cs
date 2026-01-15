using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TwinAgentsNetwork.Models
{
    /// <summary>
    /// Clase para extraer y procesar el contenido de las conversaciones del agente
    /// </summary>
    public class AgentConversationExtractor
    {
        /// <summary>
        /// Extrae el contenido procesado de un JSON de thread serializado
        /// </summary>
        /// <param name="serializedJson">JSON del thread serializado</param>
        /// <returns>Objeto con la conversación procesada</returns>
        public static TwinConversationResult ExtractConversation(string serializedJson)
        {
            try
            {
                if (string.IsNullOrEmpty(serializedJson))
                {
                    return new TwinConversationResult
                    {
                        Success = false,
                        ErrorMessage = "El JSON serializado está vacío",
                        Messages = new List<TwinMessage>()
                    };
                }

                var jsonDocument = JsonDocument.Parse(serializedJson);
                var storeState = jsonDocument.RootElement.GetProperty("storeState");
                var messages = storeState.GetProperty("messages");

                var extractedMessages = new List<TwinMessage>();

                foreach (var message in messages.EnumerateArray())
                {
                    var role = message.GetProperty("role").GetString();
                    var contents = message.GetProperty("contents");
                    
                    string extractedContent = "";
                    string authorName = "";
                    DateTime? createdAt = null;

                    // Extraer authorName si existe
                    if (message.TryGetProperty("authorName", out var authorElement))
                    {
                        authorName = authorElement.GetString() ?? "";
                    }

                    // Extraer createdAt si existe
                    if (message.TryGetProperty("createdAt", out var createdAtElement))
                    {
                        if (DateTime.TryParse(createdAtElement.GetString(), out var parsedDate))
                        {
                            createdAt = parsedDate;
                        }
                    }

                    // Extraer contenido de cada content en contents
                    foreach (var content in contents.EnumerateArray())
                    {
                        if (content.TryGetProperty("text", out var textElement))
                        {
                            var text = textElement.GetString() ?? "";
                            
                            // Si es una respuesta del asistente, extraer solo el HTML
                            if (role == "assistant" && !string.IsNullOrEmpty(text))
                            {
                                extractedContent = ExtractHtmlFromText(text);
                            }
                            else
                            {
                                // Para mensajes del usuario, mantener el texto original pero limpiarlo
                                extractedContent = CleanUserMessage(text);
                            }
                        }
                    }

                    extractedMessages.Add(new TwinMessage
                    {
                        Role = role ?? "",
                        Content = extractedContent,
                        AuthorName = authorName,
                        CreatedAt = createdAt,
                        IsHtml = role == "assistant" && extractedContent.Contains("<html")
                    });
                }

                // Obtener la última respuesta del asistente
                var lastAssistantMessage = extractedMessages
                    .Where(m => m.Role == "assistant")
                    .LastOrDefault();

                // Limpiar el JSON serializado eliminando todo el HTML
                string cleanSerializedJson = CleanHtmlFromSerializedJson(serializedJson);

                return new TwinConversationResult
                {
                    Success = true,
                    Messages = extractedMessages,
                    LastResponse = lastAssistantMessage?.Content ?? "",
                    ConversationCount = extractedMessages.Count,
                    LastAssistantResponse = lastAssistantMessage?.Content ?? "",
                    SerializedThreadJson = cleanSerializedJson
                };
            }
            catch (Exception ex)
            {
                return new TwinConversationResult
                {
                    Success = false,
                    ErrorMessage = $"Error al procesar el JSON: {ex.Message}",
                    Messages = new List<TwinMessage>()
                };
            }
        }

        /// <summary>
        /// Extrae el HTML del texto de respuesta del asistente
        /// </summary>
        private static string ExtractHtmlFromText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            // Buscar contenido HTML entre ```html y ```
            var htmlPattern = @"```html\s*([\s\S]*?)\s*```";
            var match = Regex.Match(text, htmlPattern, RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // Si no encuentra el patrón, buscar directamente por tags HTML
            var directHtmlPattern = @"<!DOCTYPE html[\s\S]*?</html>";
            var directMatch = Regex.Match(text, directHtmlPattern, RegexOptions.IgnoreCase);
            
            if (directMatch.Success)
            {
                return directMatch.Value.Trim();
            }

            // Si no encuentra HTML, devolver el texto original
            return text;
        }

        /// <summary>
        /// Limpia el mensaje del usuario removiendo caracteres de escape excesivos
        /// </summary>
        private static string CleanUserMessage(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            // Remover caracteres de escape Unicode excesivos
            text = text.Replace("\\u00E9", "é")
                      .Replace("\\u00F1", "ñ")
                      .Replace("\\u00FA", "ú")
                      .Replace("\\u00ED", "í")
                      .Replace("\\u00F3", "ó")
                      .Replace("\\u00E1", "á")
                      .Replace("\\u00FC", "ü")
                      .Replace("\\u00C9", "É")
                      .Replace("\\u00D1", "Ñ");

            // Limpiar saltos de línea excesivos
            text = Regex.Replace(text, @"\r\n|\r|\n", " ");
            text = Regex.Replace(text, @"\s+", " ");

            return text.Trim();
        }

        /// <summary>
        /// Limpia el JSON serializado eliminando todo el HTML y dejando solo texto limpio
        /// </summary>
        /// <param name="serializedJson">JSON serializado original</param>
        /// <returns>JSON serializado con HTML limpio</returns>
        private static string CleanHtmlFromSerializedJson(string serializedJson)
        {
            if (string.IsNullOrEmpty(serializedJson))
                return "";

            try
            {
                // Parsear el JSON para eliminar etiquetas HTML
                var jsonDocument = JsonDocument.Parse(serializedJson);
                var storeState = jsonDocument.RootElement.GetProperty("storeState");
                var messages = storeState.GetProperty("messages");

                var cleanedMessages = new List<object>();

                foreach (var message in messages.EnumerateArray())
                {
                    var messageObj = new Dictionary<string, object>();
                    
                    // Copiar propiedades básicas
                    if (message.TryGetProperty("role", out var roleElement))
                        messageObj["role"] = roleElement.GetString() ?? "";
                    
                    if (message.TryGetProperty("authorName", out var authorElement))
                        messageObj["authorName"] = authorElement.GetString() ?? "";
                    
                    if (message.TryGetProperty("createdAt", out var createdAtElement))
                        messageObj["createdAt"] = createdAtElement.GetString() ?? "";
                    
                    if (message.TryGetProperty("messageId", out var messageIdElement))
                        messageObj["messageId"] = messageIdElement.GetString() ?? "";

                    // Limpiar contenidos
                    if (message.TryGetProperty("contents", out var contentsElement))
                    {
                        var cleanedContents = new List<object>();
                        
                        foreach (var content in contentsElement.EnumerateArray())
                        {
                            var contentObj = new Dictionary<string, object>();
                            
                            if (content.TryGetProperty("$type", out var typeElement))
                                contentObj["$type"] = typeElement.GetString() ?? "";
                            
                            if (content.TryGetProperty("text", out var textElement))
                            {
                                var originalText = textElement.GetString() ?? "";
                                // Limpiar HTML del texto
                                var cleanText = StripHtmlTags(originalText);
                                contentObj["text"] = cleanText;
                            }
                            
                            cleanedContents.Add(contentObj);
                        }
                        
                        messageObj["contents"] = cleanedContents;
                    }
                    
                    cleanedMessages.Add(messageObj);
                }

                // Reconstruir el objeto JSON limpio
                var cleanedObject = new
                {
                    storeState = new
                    {
                        messages = cleanedMessages
                    }
                };

                return JsonSerializer.Serialize(cleanedObject, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error limpiando HTML del JSON: {ex.Message}");
                return serializedJson; // Devolver original si hay error
            }
        }

        /// <summary>
        /// Elimina todas las etiquetas HTML de un texto y deja solo el contenido limpio
        /// </summary>
        /// <param name="html">Texto con HTML</param>
        /// <returns>Texto limpio sin HTML</returns>
        private static string StripHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            // Remover bloques de código HTML entre ```html y ```
            html = Regex.Replace(html, @"```html\s*([\s\S]*?)\s*```", (match) =>
            {
                var htmlContent = match.Groups[1].Value;
                return StripHtmlTagsFromContent(htmlContent);
            }, RegexOptions.IgnoreCase);

            // Remover etiquetas HTML directas
            html = StripHtmlTagsFromContent(html);

            // Limpiar caracteres de escape Unicode
            html = html.Replace("\\u00E9", "é")
                      .Replace("\\u00F1", "ñ")
                      .Replace("\\u00FA", "ú")
                      .Replace("\\u00ED", "í")
                      .Replace("\\u00F3", "ó")
                      .Replace("\\u00E1", "á")
                      .Replace("\\u00FC", "ü")
                      .Replace("\\u00C9", "É")
                      .Replace("\\u00D1", "Ñ");

            // Limpiar saltos de línea excesivos y espacios múltiples
            html = Regex.Replace(html, @"\r\n|\r|\n", " ");
            html = Regex.Replace(html, @"\s+", " ");

            return html.Trim();
        }

        /// <summary>
        /// Elimina las etiquetas HTML de un contenido específico
        /// </summary>
        private static string StripHtmlTagsFromContent(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return "";

            // Eliminar comentarios HTML
            htmlContent = Regex.Replace(htmlContent, @"<!--[\s\S]*?-->", "", RegexOptions.IgnoreCase);

            // Eliminar scripts y estilos completos
            htmlContent = Regex.Replace(htmlContent, @"<(script|style)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);

            // Extraer texto de elementos específicos preservando estructura
            htmlContent = Regex.Replace(htmlContent, @"<(h[1-6]|p|div|section|li)[^>]*>([\s\S]*?)</\1>", (match) =>
            {
                var content = match.Groups[2].Value;
                content = Regex.Replace(content, @"<[^>]+>", ""); // Remover tags internos
                return content.Trim() + " ";
            }, RegexOptions.IgnoreCase);

            // Extraer texto de tablas
            htmlContent = Regex.Replace(htmlContent, @"<(td|th)[^>]*>([\s\S]*?)</\1>", (match) =>
            {
                var content = match.Groups[2].Value;
                content = Regex.Replace(content, @"<[^>]+>", "");
                return content.Trim() + " | ";
            }, RegexOptions.IgnoreCase);

            // Convertir saltos de línea de HTML
            htmlContent = Regex.Replace(htmlContent, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);

            // Eliminar todas las etiquetas HTML restantes
            htmlContent = Regex.Replace(htmlContent, @"<[^>]+>", "");

            // Decodificar entidades HTML
            htmlContent = htmlContent.Replace("&lt;", "<")
                                   .Replace("&gt;", ">")
                                   .Replace("&amp;", "&")
                                   .Replace("&quot;", "\"")
                                   .Replace("&#39;", "'")
                                   .Replace("&nbsp;", " ");

            return htmlContent;
        }
    }

    /// <summary>
    /// Resultado de la extracción de conversación
    /// </summary>
    public class TwinConversationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<TwinMessage> Messages { get; set; } = new();
        public string LastResponse { get; set; } = "";
        public string LastAssistantResponse { get; set; } = "";
        public int ConversationCount { get; set; }
        public string SerializedThreadJson { get; set; } = "";
    }

    /// <summary>
    /// Mensaje individual en la conversación
    /// </summary>
    public class TwinMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
        public bool IsHtml { get; set; }
    }
}