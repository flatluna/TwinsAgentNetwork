using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TwinAgentsNetwork.Services;
using TwinAgentsLibrary.Models;
using Newtonsoft.Json;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente de salud que analiza métricas de salud y genera recomendaciones personalizadas
    /// Utiliza OpenAI para crear un análisis detallado basado en las métricas actuales del usuario
    /// </summary>
    public class AgentTwinHealth
    {
        private readonly AgentHealthCosmosDB _healthCosmosService;
        private readonly ILogger<AgentTwinHealth> _logger;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentTwinHealth(
            AgentHealthCosmosDB healthCosmosService,
            ILogger<AgentTwinHealth> logger)
        {
            _healthCosmosService = healthCosmosService;
            _logger = logger;
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        /// <summary>
        /// Obtiene las métricas de salud de un usuario y genera recomendaciones personalizadas
        /// basadas en su estado actual de salud. Utiliza OpenAI para analizar y crear un plan
        /// de recomendaciones detallado.
        /// </summary>
        /// <param name="metricsId">ID de las métricas de salud a analizar</param>
        /// <param name="twinId">ID del usuario Twin</param>
        /// <returns>Objeto Health con recomendaciones personalizadas basadas en sus métricas</returns>
        public async Task<Health> GetHealthRecommendationsAsync(string metricsId, string twinId)
        {
            if (string.IsNullOrEmpty(metricsId))
                throw new ArgumentException("MetricsId cannot be null or empty", nameof(metricsId));

            if (string.IsNullOrEmpty(twinId))
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));

            try
            {
                _logger.LogInformation("?? Obteniendo métricas de salud para análisis. MetricsId: {MetricsId}, TwinID: {TwinID}", 
                    metricsId, twinId);

                // 1. Obtener las métricas de salud del usuario
                var healthMetrics = await _healthCosmosService.GetHealthMetricsAsync(metricsId, twinId);

                if (healthMetrics == null)
                {
                    _logger.LogError("? No se encontraron métricas de salud para el usuario. MetricsId: {MetricsId}", metricsId);
                    throw new InvalidOperationException($"Health metrics not found for Id: {metricsId}");
                }

                _logger.LogInformation("? Métricas de salud obtenidas. Iniciando análisis con OpenAI...");

                // 2. Crear agente de OpenAI para análisis
                var aiClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential());

                var chatClient = aiClient.GetChatClient(_azureOpenAIModelName);

                AIAgent analysisAgent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateHealthAnalysisInstructions(),
                    name: "HealthAnalysisAgent");

                // 3. Preparar el prompt con los datos del usuario
                string analysisPrompt = CreateHealthAnalysisPrompt(healthMetrics);

                _logger.LogInformation("?? Enviando datos de salud a OpenAI para análisis...");

                // 4. Obtener análisis de OpenAI
                var response = await analysisAgent.RunAsync(analysisPrompt);

                _logger.LogInformation("? Análisis completado. Procesando respuesta...");

                // 5. Parsear la respuesta de OpenAI como JSON y crear objeto Health
                var health = CreateHealthRecommendationObject(response.Text, healthMetrics, twinId);

                _logger.LogInformation("? Recomendaciones de salud generadas exitosamente. TwinID: {TwinID}", twinId);

                return health;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error generando recomendaciones de salud. TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Obtiene las métricas de salud más recientes de un usuario y genera recomendaciones
        /// </summary>
        /// <param name="twinId">ID del usuario Twin</param>
        /// <returns>Objeto Health con recomendaciones basadas en las métricas más recientes</returns>
        public async Task<Health> GetLatestHealthRecommendationsAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));

            try
            {
                _logger.LogInformation("?? Obteniendo últimas métricas de salud para TwinID: {TwinID}", twinId);

                // Obtener la métrica más reciente
                var latestMetrics = await _healthCosmosService.GetLatestHealthMetricsAsync(twinId);

                if (latestMetrics == null)
                {
                    _logger.LogError("? No se encontraron métricas de salud para el usuario. TwinID: {TwinID}", twinId);
                    throw new InvalidOperationException($"No health metrics found for Twin: {twinId}");
                }

                // Generar recomendaciones basadas en las métricas más recientes
                return await GetHealthRecommendationsAsync(latestMetrics.Id, twinId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error obteniendo recomendaciones de salud más recientes. TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Crea las instrucciones para el agente de análisis de salud
        /// </summary>
        private string CreateHealthAnalysisInstructions()
        {
            return @"
Eres un especialista en salud y nutrición con experiencia en análisis de métricas de salud y generación de recomendaciones personalizadas.

TU ROL:
- Analizar métricas de salud (peso, altura, edad, género, IMC, colesterol, glucosa, presión arterial)
- Calcular índices nutricionales y metabólicos (TMB, gasto calórico, déficit necesario)
- Evaluar riesgos de salud y prioridades nutricionales
- Generar recomendaciones detalladas, específicas y alcanzables
- Crear planes de acción realistas con metas a corto y largo plazo

REQUERIMIENTOS DE RESPUESTA:
1. ANÁLISIS DETALLADO: Proporciona un análisis completo en formato JSON
2. CÁLCULOS PRECISOS: Incluye fórmulas nutricionales (Mifflin-St Jeor, etc)
3. RECOMENDACIONES PERSONALIZADAS: Específicas para el perfil del usuario
4. ESTRUCTURA JSON: Sigue exactamente la estructura especificada
5. CLARIDAD: Explicaciones claras pero técnicas cuando sea necesario
6. ACCIONABLE: Recomendaciones que el usuario pueda implementar

RESPONDE SIEMPRE EN FORMATO JSON VÁLIDO.";
        }

        /// <summary>
        /// Crea el prompt con los datos de salud del usuario para enviar a OpenAI
        /// </summary>
        private string CreateHealthAnalysisPrompt(HealthMetrics metrics)
        {
            var metricsJson = JsonConvert.SerializeObject(metrics, Formatting.Indented);

            return $@"
Analiza las siguientes métricas de salud y genera un plan de recomendaciones personalizado en formato JSON.

MÉTRICAS DE SALUD DEL USUARIO:
{metricsJson}

INSTRUCCIONES ESPECÍFICAS:
1. Calcula TMB (Tasa Metabólica Basal) usando Mifflin-St Jeor
2. Determina gasto calórico diario con actividad leve (factor 1.2)
3. Si hay objetivo de peso, calcula el déficit calórico necesario
4. Analiza cada métrica de sangre respecto a rangos normales
5. Identifica riesgos y prioridades nutricionales
6. Crea objetivos de control específicos para el usuario
7. Observa los objetivos de la persoana y en reporte detallado indica qeu hacer para lograr esos objetivos
recomienda cuantas calorias comer basado en los objetivos en tiempo y cantidad de peso al igual que la edad, genero , etc.
IMPORTANTE nunca comiences con ```json ni termines con ``` solo responde con el JSON

ESTRUCTURA JSON REQUERIDA (IMPORTANTE - Mantén esta estructura exacta):
{{
  ""ReporteDetallado"": ""Análisis completo de las métricas y recomendaciones..."",
  ""CaloriasDiariasRecomendadas"": 2432,
  ""CaloriasDiariasObjetivo"": 1924,
  ""ProteinasDiariasRecomendadas"": 120,
  ""ProteinasDiariasObjetivo"": 140,
  ""CarbohidratosDiariosRecomendados"": 250,
  ""CarbohidratosDiariosObjetivo"": 200,
  ""GrasasDiariasRecomendadas"": 80,
  ""GrasasDiariasObjetivo"": 65,
  ""AguaDiariaObjetivo"": 3.0,
  ""SodioDiarioRecomendado"": 1700,
  ""NotasAdicionales"": ""Notas de nutrición específicas..."",
  ""RecomendacionesPersonalizadas"": [""Recomendación 1"", ""Recomendación 2""],
  ""AnalisisRiesgo"": ""bajo|medio|alto"",
  ""PrioridadesNutricionales"": [""Prioridad 1"", ""Prioridad 2""],
  ""AnalisisPeso"": {{
    ""PesoActual"": 120,
    ""PesoRecomendadoMinimo"": 75,
    ""PesoRecomendadoMaximo"": 92,
    ""PesoObjetivo"": 110,
    ""EstadoPesoActual"": ""Descripción del estado actual..."",
    ""EstrategiasParaObjetivo"": [""Estrategia 1"", ""Estrategia 2""]
  }},
  ""AnalisisMetricasSangre"": [
    {{
      ""NombreMetrica"": ""Colesterol Total"",
      ""ValorActual"": ""180 mg/dL"",
      ""ValorRecomendado"": ""<200 mg/dL"",
      ""EstadoActual"": ""Óptimo|Normal|Elevado"",
      ""Recomendaciones"": [""Recomendación 1""],
      ""PrioridadAtencion"": ""bajo|medio|alto""
    }}
  ],
  ""ResumenEjecutivo"": ""Resumen breve del estado general y principales recomendaciones..."",
  ""MetasCortoPlazos"": [""Meta 1"", ""Meta 2""],
  ""MetasLargoPlazos"": [""Meta 1"", ""Meta 2""],
  ""ObjetivosControlUsuario"": [
    {{
      ""Objetivo"": ""Nombre del objetivo"",
      ""Descripcion"": ""Descripción detallada"",
      ""UnidadMedida"": ""Calorías|Gramos|Litros|Horas"",
      ""ValorObjetivo"": 1924,
      ""Recomendaciones"": [""Recomendación 1""]
    }}
  ]
}}

RESPONDE SOLAMENTE CON EL JSON VÁLIDO, sin explicaciones adicionales.";
        }

        /// <summary>
        /// Crea un objeto Health con las recomendaciones generadas por OpenAI
        /// Parsea la respuesta JSON y la serializa en la clase Health usando Newtonsoft.Json
        /// </summary>
        private Health CreateHealthRecommendationObject(string aiResponse, HealthMetrics metrics, string twinId)
        {
            try
            {
                _logger.LogInformation("?? Parseando respuesta de OpenAI...");

                // Parsear la respuesta JSON de OpenAI
                var recommendationsData = JsonConvert.DeserializeObject<dynamic>(aiResponse);

                // Crear objeto Health
                var health = new Health
                {
                    Id = Guid.NewGuid().ToString(),
                    TwinID = twinId,
                    FechaCreacion = DateTime.UtcNow,
                    Tipo = "HealthRecommendation",
                    Version = "1.0",
                    Recomendaciones = new Recomendaciones
                    {
                        ReporteDetallado = recommendationsData?["ReporteDetallado"]?.ToString() ?? string.Empty,
                        CaloriasDiariasRecomendadas = ParseDouble(recommendationsData?["CaloriasDiariasRecomendadas"]),
                        CaloriasDiariasObjetivo = ParseDouble(recommendationsData?["CaloriasDiariasObjetivo"]),
                        ProteinasDiariasRecomendadas = ParseDouble(recommendationsData?["ProteinasDiariasRecomendadas"]),
                        ProteinasDiariasObjetivo = ParseDouble(recommendationsData?["ProteinasDiariasObjetivo"]),
                        CarbohidratosDiariosRecomendados = ParseDouble(recommendationsData?["CarbohidratosDiariosRecomendados"]),
                        CarbohidratosDiariosObjetivo = ParseDouble(recommendationsData?["CarbohidratosDiariosObjetivo"]),
                        GrasasDiariasRecomendadas = ParseDouble(recommendationsData?["GrasasDiariasRecomendadas"]),
                        GrasasDiariasObjetivo = ParseDouble(recommendationsData?["GrasasDiariasObjetivo"]),
                        AguaDiariaObjetivo = ParseDouble(recommendationsData?["AguaDiariaObjetivo"]),
                        SodioDiarioRecomendado = ParseDouble(recommendationsData?["SodioDiarioRecomendado"]),
                        NotasAdicionales = recommendationsData?["NotasAdicionales"]?.ToString() ?? string.Empty,
                        RecomendacionesPersonalizadas = ParseStringList(recommendationsData?["RecomendacionesPersonalizadas"]),
                        AnalisisRiesgo = recommendationsData?["AnalisisRiesgo"]?.ToString() ?? "medio",
                        PrioridadesNutricionales = ParseStringList(recommendationsData?["PrioridadesNutricionales"]),
                        AnalisisPeso = ParseAnalisisPeso(recommendationsData?["AnalisisPeso"]),
                        AnalisisMetricasSangre = ParseMetricasSangre(recommendationsData?["AnalisisMetricasSangre"]),
                        ResumenEjecutivo = recommendationsData?["ResumenEjecutivo"]?.ToString() ?? string.Empty,
                        MetasCortoPlazos = ParseStringList(recommendationsData?["MetasCortoPlazos"]),
                        MetasLargoPlazos = ParseStringList(recommendationsData?["MetasLargoPlazos"]),
                        ObjetivosControlUsuario = ParseObjetivosControl(recommendationsData?["ObjetivosControlUsuario"])
                    }
                };

                _logger.LogInformation("? Objeto Health creado exitosamente. Id: {Id}", health.Id);
                return health;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error parseando respuesta de OpenAI");
                throw new InvalidOperationException("Error parsing health recommendations from AI response", ex);
            }
        }

        /// <summary>
        /// Parsea un valor double desde la respuesta dinámica
        /// </summary>
        private double ParseDouble(dynamic value)
        {
            try
            {
                if (value == null) return 0;
                return Convert.ToDouble(value);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Parsea una lista de strings desde la respuesta dinámica
        /// </summary>
        private List<string> ParseStringList(dynamic value)
        {
            var list = new List<string>();
            try
            {
                if (value == null) return list;
                
                foreach (var item in value)
                {
                    list.Add(item.ToString());
                }
            }
            catch { }
            
            return list;
        }

        /// <summary>
        /// Parsea AnalisisPeso desde la respuesta dinámica
        /// </summary>
        private AnalisisPeso ParseAnalisisPeso(dynamic value)
        {
            var analisis = new AnalisisPeso();
            try
            {
                if (value == null) return analisis;

                analisis.PesoActual = ParseDouble(value["PesoActual"]);
                analisis.PesoRecomendadoMinimo = ParseDouble(value["PesoRecomendadoMinimo"]);
                analisis.PesoRecomendadoMaximo = ParseDouble(value["PesoRecomendadoMaximo"]);
                analisis.PesoObjetivo = ParseDouble(value["PesoObjetivo"]);
                analisis.EstadoPesoActual = value["EstadoPesoActual"]?.ToString() ?? string.Empty;
                analisis.EstrategiasParaObjetivo = ParseStringList(value["EstrategiasParaObjetivo"]);
            }
            catch { }
            
            return analisis;
        }

        /// <summary>
        /// Parsea la lista de métricas de sangre desde la respuesta dinámica
        /// </summary>
        private List<MetricaSangre> ParseMetricasSangre(dynamic value)
        {
            var lista = new List<MetricaSangre>();
            try
            {
                if (value == null) return lista;

                foreach (var item in value)
                {
                    var metrica = new MetricaSangre
                    {
                        NombreMetrica = item["NombreMetrica"]?.ToString() ?? string.Empty,
                        ValorActual = item["ValorActual"]?.ToString() ?? string.Empty,
                        ValorRecomendado = item["ValorRecomendado"]?.ToString() ?? string.Empty,
                        EstadoActual = item["EstadoActual"]?.ToString() ?? string.Empty,
                        Recomendaciones = ParseStringList(item["Recomendaciones"]),
                        PrioridadAtencion = item["PrioridadAtencion"]?.ToString() ?? "bajo"
                    };
                    lista.Add(metrica);
                }
            }
            catch { }
            
            return lista;
        }

        /// <summary>
        /// Parsea la lista de objetivos de control desde la respuesta dinámica
        /// </summary>
        private List<ObjetivoControl> ParseObjetivosControl(dynamic value)
        {
            var lista = new List<ObjetivoControl>();
            try
            {
                if (value == null) return lista;

                foreach (var item in value)
                {
                    var objetivo = new ObjetivoControl
                    {
                        Objetivo = item["Objetivo"]?.ToString() ?? string.Empty,
                        Descripcion = item["Descripcion"]?.ToString() ?? string.Empty,
                        UnidadMedida = item["UnidadMedida"]?.ToString() ?? string.Empty,
                        ValorObjetivo = item["ValorObjetivo"],
                        Recomendaciones = ParseStringList(item["Recomendaciones"])
                    };
                    lista.Add(objetivo);
                }
            }
            catch { }
            
            return lista;
        }
    }
}
