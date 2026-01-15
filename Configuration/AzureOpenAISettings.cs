using System;

namespace TwinAgentsNetwork.Configuration
{
    /// <summary>
    /// Configuración centralizada para Azure OpenAI.
    /// TODOS los agentes deben usar esta clase para obtener configuración.
    /// NO se permiten URLs o keys hardcodeadas en el código.
    /// </summary>
    public static class AzureOpenAISettings
    {
        /// <summary>
        /// Obtiene el endpoint de Azure OpenAI desde variables de entorno.
        /// </summary>
        /// <exception cref="InvalidOperationException">Si la variable no está configurada.</exception>
        public static string Endpoint
        {
            get
            {
                var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                if (string.IsNullOrEmpty(endpoint))
                {
                    throw new InvalidOperationException(
                        "AZURE_OPENAI_ENDPOINT environment variable is not configured. " +
                        "Please set it in local.settings.json or Azure App Settings.");
                }
                return endpoint;
            }
        }

        /// <summary>
        /// Obtiene la API key de Azure OpenAI desde variables de entorno.
        /// </summary>
        /// <exception cref="InvalidOperationException">Si la variable no está configurada.</exception>
        public static string ApiKey
        {
            get
            {
                var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException(
                        "AZURE_OPENAI_API_KEY environment variable is not configured. " +
                        "Please set it in local.settings.json or Azure App Settings.");
                }
                return apiKey;
            }
        }

        /// <summary>
        /// Obtiene el nombre del modelo de chat desde variables de entorno.
        /// </summary>
        /// <exception cref="InvalidOperationException">Si la variable no está configurada.</exception>
        public static string ChatModelName
        {
            get
            {
                var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") 
                    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_MODEL");
                if (string.IsNullOrEmpty(modelName))
                {
                    throw new InvalidOperationException(
                        "AZURE_OPENAI_MODEL_NAME environment variable is not configured. " +
                        "Please set it in local.settings.json or Azure App Settings.");
                }
                return modelName;
            }
        }

        /// <summary>
        /// Obtiene el nombre del deployment de embedding desde variables de entorno.
        /// </summary>
        public static string EmbeddingDeploymentName
        {
            get
            {
                var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME");
                if (string.IsNullOrEmpty(deploymentName))
                {
                    throw new InvalidOperationException(
                        "AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME environment variable is not configured. " +
                        "Please set it in local.settings.json or Azure App Settings.");
                }
                return deploymentName;
            }
        }

        /// <summary>
        /// Obtiene la versión de la API de Azure OpenAI.
        /// Retorna "2023-03-15-preview" si no está configurada.
        /// </summary>
        public static string ApiVersion
        {
            get
            {
                return Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") 
                    ?? "2023-03-15-preview";
            }
        }

        /// <summary>
        /// Intenta obtener el endpoint sin lanzar excepción.
        /// </summary>
        /// <param name="endpoint">El endpoint si está configurado.</param>
        /// <returns>True si está configurado, false si no.</returns>
        public static bool TryGetEndpoint(out string endpoint)
        {
            endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "";
            return !string.IsNullOrEmpty(endpoint);
        }

        /// <summary>
        /// Intenta obtener la API key sin lanzar excepción.
        /// </summary>
        /// <param name="apiKey">La API key si está configurada.</param>
        /// <returns>True si está configurada, false si no.</returns>
        public static bool TryGetApiKey(out string apiKey)
        {
            apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? "";
            return !string.IsNullOrEmpty(apiKey);
        }

        /// <summary>
        /// Intenta obtener el nombre del modelo sin lanzar excepción.
        /// </summary>
        /// <param name="modelName">El nombre del modelo si está configurado.</param>
        /// <returns>True si está configurado, false si no.</returns>
        public static bool TryGetChatModelName(out string modelName)
        {
            modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") 
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_MODEL") 
                ?? "";
            return !string.IsNullOrEmpty(modelName);
        }

        /// <summary>
        /// Valida que todas las configuraciones requeridas estén presentes.
        /// </summary>
        /// <exception cref="InvalidOperationException">Si alguna configuración falta.</exception>
        public static void ValidateConfiguration()
        {
            var missingConfigs = new System.Collections.Generic.List<string>();

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")))
                missingConfigs.Add("AZURE_OPENAI_ENDPOINT");

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")))
                missingConfigs.Add("AZURE_OPENAI_API_KEY");

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME")) 
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_MODEL")))
                missingConfigs.Add("AZURE_OPENAI_MODEL_NAME or AZURE_OPENAI_CHAT_MODEL");

            if (missingConfigs.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Missing required Azure OpenAI configuration: {string.Join(", ", missingConfigs)}. " +
                    "Please configure in local.settings.json or Azure App Settings.");
            }
        }
    }
}
