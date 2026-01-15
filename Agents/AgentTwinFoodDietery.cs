using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinAgentsLibrary.Models;
using TwinFx.Agents;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.Agents
{
    public class AgentTwinFoodDietery
    {
        private readonly AiWebSearchAgent _aiWebSearchAgent;
        private readonly AgentNutritionCosmosDB _cosmosService;
        private readonly ILogger<AgentTwinFoodDietery> _logger;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentTwinFoodDietery(
            AiWebSearchAgent aiWebSearchAgent, 
            ILogger<AgentTwinFoodDietery> logger)
        {
            _aiWebSearchAgent = aiWebSearchAgent;
            _logger = logger;
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        public AgentTwinFoodDietery(
            AiWebSearchAgent aiWebSearchAgent, 
            AgentNutritionCosmosDB cosmosService,
            ILogger<AgentTwinFoodDietery> logger)
        {
            _aiWebSearchAgent = aiWebSearchAgent;
            _cosmosService = cosmosService;
            _logger = logger;
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        /// <summary>
        /// Obtiene información nutricional completa de un alimento usando búsqueda con Bing Grounding
        /// </summary>
        public async Task<FoodDiaryEntry> GetFoodNutritionInfoAsync(string foodDescription, string twinId)
        {
            if (string.IsNullOrEmpty(foodDescription))
            {
                throw new ArgumentException("Food description cannot be null or empty", nameof(foodDescription));
            }

            try
            {
                _logger.LogInformation("?? Buscando información nutricional para: {FoodDescription}", foodDescription);

                var searchPrompt = $@"Busca información nutricional DETALLADA, VERIFICADA Y CONFIABLE sobre el alimento: '{foodDescription}'

INSTRUCCIONES IMPORTANTES:
1. Obtén datos de fuentes confiables (USDA, bases de datos nutricionales oficiales, tablas españolas)
2. Incluye una DESCRIPCIÓN COMPLETA del alimento con su categoría
3. Proporciona FUENTES VERIFICABLES al final
4. Si hay múltiples fuentes, cita al menos 2-3 sitios confiables

DESCRIPCIÓN DEL ALIMENTO:
- Descripción detallada: Proporciona una descripción completa del alimento, su origen, características principales, usos comunes
- Beneficios nutricionales clave
- Propiedades organolépticas (sabor, textura, aroma)

DATOS NUTRICIONALES REQUERIDOSTYPICAL SERVING:
- Calorías: por porción típica y por 100g
- Unidad de medida común: identifica si es gramos (g), mililitros (ml), pieza, taza, cucharada
- Cantidad en unidad común: especifica la cantidad

MACRONUTRIENTES (en gramos o mg según corresponda):
- Proteínas (g)
- Carbohidratos totales (g)
- Grasas totales (g)
- Grasas saturadas (g)
- Grasas monoinsaturadas (g)
- Grasas poliinsaturadas (g)
- Colesterol (mg)
- Fibra dietética (g)
- Azúcares totales (g)

MINERALES (en mg o mcg según corresponda):
- Sodio (mg)
- Potasio (mg)
- Calcio (mg)
- Hierro (mg)
- Magnesio (mg)
- Fósforo (mg)
- Zinc (mg)
- Cobre (mg)
- Manganeso (mg)
- Selenio (mcg)

VITAMINAS (en mg o mcg según corresponda):
- Vitamina A (mcg)
- Vitamina C (mg)
- Vitamina D (mcg)
- Vitamina E (mg)
- Vitamina K (mcg)
- Tiamina/Vitamina B1 (mg)
- Riboflavina/Vitamina B2 (mg)
- Niacina/Vitamina B3 (mg)
- Vitamina B6 (mg)
- Folato (mcg)
- Vitamina B12 (mcg)

INFORMACIÓN ADICIONAL:
- Categoría del alimento
- Componentes principales
- Índice glucémico
- Carga glucémica

RETORNA SOLO EL JSON con esta estructura exacta:
- food: nombre del alimento
- foodDescription: descripción detallada completa
- source: URLs y fuentes confiables
- categoria: categoría del alimento
- atributosComida: ARRAY de objetos con nombre y value para cada atributo nutricional
- indiceSaciedad: objeto con valores para vegetales, proteinas, carbohidratos, grasas
- componentes: array de componentes principales

ESTRUCTURA DE ATRIBUTOS DE COMIDA:

Cada atributo debe ser un objeto con las siguientes características:

nombre: string que representa el nombre del atributo (ej. typicalServing, unidadComun, cantidadComun, caloriesPerTypicalServing, etc.)
value: número, string, o null, según corresponda.
VALORES DE ATRIBUTOS A INCLUIR:
typicalServing, unidadComun, cantidadComun, caloriesPerTypicalServing, caloriasUnidadComun, caloriasPor100g, caloriesPerCup, caloriesPerTbsp, caloriesPerPiece, proteinas, carbohidratos, grasas, grasasSaturadas, grasasMonoinsaturadas, grasasPoliinsaturadas, colesterol, fibra, azucares, sodio, potasio, calcio, hierro, vitaminaA, vitaminaC, vitaminaD, vitaminaE, vitaminaK, tiamina, riboflavina, niacina, vitaminaB6, folato, vitaminaB12, magnesio, fosforo, zinc, cobre, manganeso, selenio, indiceGlucemico, cargaGlucemica.

IMPORTANTE: Usa el siguiente formato como guía. Asegúrate de generar exactamente los mismos campos, pero con la información específica del alimento solicitado.

CALIFICACIÓN Y DESCRIPCIÓN:

Determina un score del 1 al 5, donde 1 es muy malo y 5 es muy bueno. El score debe ser un número, no un string.
En el campo ""descripcionScore"" proporciona una explicación detallada de las razones detrás de tu puntuación, incluyendo una revisión de los atributos nutricionales, beneficios, pros y contras del alimento.
REGLAS PARA EL CAMPO source:
USA SOLO NUMEROS NMUNCA NULL o 0 pero no NULL: 
       ""vegetales"": 1,  
      ""proteinas"": 1,  
      ""carbohidratos"": 1,  
      ""grasas"": 1  
Formato de URLs: Asegura que l 

en el source no pongas el url solo explica y describe de donde lo sacaste nobre de la empresa o site.
no pongas url por que da problemas en JSON
pon atencion is es un alimento combinado suma todo en todos los datos nutricionales y pon el desglose de cada componente en un array adicional llamado ""platoCombinado"" con nombre, cantidad y calorias de cada componente.
IMPORTANTE: no inventes nuevos datos en el json usa exactamente solo os campos que estan abajo en el ejemplo.

EJEMPLO DE RESPUESTA JSON EXACTA A RETORNAR:
********************** START ******************
{{  
  ""foodStats"": {{  
    ""food"": ""Plato combinado de fresas, plátano y otros"",  
    ""foodDescription"": ""Un delicioso plato combinado que incluye fresas, un plátano, cocoa en polvo, yogur, miel y mermelada de fresas. Ideal como desayuno o snack energético."",  
    ""source"": [""USDA"", ""Fundación Española de Nutrición""],  
    ""categoria"": ""plato combinado"",  
    ""score"": 4,  
    ""descripcionScore"": ""Este plato es nutritivo y balanceado, ofreciendo una mezcla de frutas frescas, carbohidratos y azúcares naturales. Sin embargo, el contenido de azúcares puede ser alto debido a la miel y la mermelada."",  
    ""atributosComida"": [  
      {{  
        ""nombre"": ""typicalServing"",  
        ""value"": ""1 plato""  
      }},  
      {{  
        ""nombre"": ""unidadComun"",  
        ""value"": ""plato""  
      }},  
      {{  
        ""nombre"": ""cantidadComun"",  
        ""value"": 1  
      }},  
      {{  
        ""nombre"": ""caloriesPerTypicalServing"",  
        ""value"": 397  
      }},  
      {{  
        ""nombre"": ""caloriasUnidadComun"",  
        ""value"": 397  
      }},  
      {{  
        ""nombre"": ""caloriasPor100g"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""caloriesPerCup"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""caloriesPerTbsp"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""caloriesPerPiece"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""proteinas"",  
        ""value"": 3.5  
      }},  
      {{  
        ""nombre"": ""carbohidratos"",  
        ""value"": 93.1  
      }},  
      {{  
        ""nombre"": ""grasas"",  
        ""value"": 1.0  
      }},  
      {{  
        ""nombre"": ""grasasSaturadas"",  
        ""value"": 0.2  
      }},  
      {{  
        ""nombre"": ""grasasMonoinsaturadas"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""grasasPoliinsaturadas"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""colesterol"",  
        ""value"": 0  
      }},  
      {{  
        ""nombre"": ""fibra"",  
        ""value"": 4.3  
      }},  
      {{  
        ""nombre"": ""azucares"",  
        ""value"": 62.0  
      }},  
      {{  
        ""nombre"": ""sodio"",  
        ""value"": 1  
      }},  
      {{  
        ""nombre"": ""potasio"",  
        ""value"": 522  
      }},  
      {{  
        ""nombre"": ""calcio"",  
        ""value"": 50  
      }},  
      {{  
        ""nombre"": ""hierro"",  
        ""value"": 0.5  
      }},  
      {{  
        ""nombre"": ""vitaminaA"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""vitaminaC"",  
        ""value"": 60  
      }},  
      {{  
        ""nombre"": ""vitaminaD"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""vitaminaE"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""vitaminaK"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""tiamina"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""riboflavina"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""niacina"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""vitaminaB6"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""folato"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""vitaminaB12"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""magnesio"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""fosforo"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""zinc"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""cobre"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""manganeso"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""selenio"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""indiceGlucemico"",  
        ""value"": null  
      }},  
      {{  
        ""nombre"": ""cargaGlucemica"",  
        ""value"": null  
      }}  
    ],  
    ""componentes"": [  
      {{  
        ""nombreComponente"": ""agua"",  
        ""unidadMedida"": ""g"",  
        ""cantidad"": 74  
      }},  
      {{  
        ""nombreComponente"": ""carbohidratos"",  
        ""unidadMedida"": ""g"",  
        ""cantidad"": 24  
      }},  
      {{  
        ""nombreComponente"": ""proteínas"",  
        ""unidadMedida"": ""g"",  
        ""cantidad"": 1.09  
      }} ],
    ""platoCombinado"": [  
      {{  
        ""nombre"": ""fresas"",  
        ""cantidad"": ""6"",  
        ""calorias"": ""36""  
      }},  
      {{  
        ""nombre"": ""plátano"",  
        ""cantidad"": ""6"",  
        ""calorias"": ""135""  
      }},  
      {{  
        ""nombre"": ""cocoa en polvo"",  
        ""cantidad"":  ""6 cucharas"",  
        ""calorias"": ""12""  
      }} 
    ]  
  }}  
}}  
*********************** END ******************

INSTRUCCIONES CRÍTICAS FINALES:

IMPORTANTE: Calcula los atributos nutricionales con precisión 
dependiendo de la cantidad que se pregunta. Ejemplo 3 bananas multiplicas todo por tres. etc..
Aseurate de siempre indicar el valor nutricional de calotias por una unidad. 
- SOLO RETORNA JSON VÁLIDO
- NO incluyas explicaciones fuera del JSON
- NO incluyas ```json o ``` markdown
- Si datos no están disponibles, usa 0 o null
- Información verificada de fuentes oficiales
- Descripción debe ser detallada y profesional


MUY IMPORTANTE VALIDA EL JSON
- Fuentes específicas y verificables


Ejecuta ahora el proceso te espero.....";

                string bingResults = await _aiWebSearchAgent.BingGroundingSearchAsync(searchPrompt);

                _logger.LogInformation("? Búsqueda completada para: {FoodDescription}", foodDescription);

                try
                {
                    var foodStats = JsonConvert.DeserializeObject<FoodDiaryResponse>(bingResults);

                    var foodDiaryEntry = new FoodDiaryEntry
                    {
                        BingResults = bingResults,
                        TwinID = twinId,
                        DiaryFood = foodDescription,
                        DateTimeConsumed = DateTime.UtcNow,
                        DateTimeCreated = DateTime.UtcNow,
                        Id = Guid.NewGuid().ToString(),
                        Time = DateTime.UtcNow.ToString("HH:mm:ss"),
                        
                        FoodStats = foodStats.FoodStats

                    };

                    return foodDiaryEntry;

                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, "? Error obteniendo información nutricional para: {FoodDescription}", foodDescription);
                    FoodDiaryEntry food = new FoodDiaryEntry
                    {
                        BingResults = null,
                        TwinID = twinId,
                        DiaryFood = foodDescription,
                        DateTimeConsumed = DateTime.UtcNow,
                        DateTimeCreated = DateTime.UtcNow,
                        Id = Guid.NewGuid().ToString(),
                        Time = DateTime.UtcNow.ToString("HH:mm:ss"), 
                        FoodStats = null
                    };
                    return food;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error obteniendo información nutricional para: {FoodDescription}", foodDescription);
                FoodDiaryEntry food = new FoodDiaryEntry
                {
                    BingResults = null,
                    TwinID = twinId,
                    DiaryFood = foodDescription,
                    DateTimeConsumed = DateTime.UtcNow,
                    DateTimeCreated = DateTime.UtcNow,
                    Id = Guid.NewGuid().ToString(),
                    Time = DateTime.UtcNow.ToString("HH:mm:ss"),
                    
                    FoodStats = null
                };
                return food;
            }
        }

        /// <summary>
        /// Obtiene la respuesta a una pregunta de nutrición basada en los alimentos consumidos en un día específico.
        /// Calcula los totales nutricionales de todos los alimentos consumidos y utiliza OpenAI para responder la pregunta.
        /// </summary>
        /// <param name="twinId">El identificador único del usuario (Twin)</param>
        /// <param name="year">Año de la consulta (ejemplo: 2025)</param>
        /// <param name="month">Mes de la consulta (1-12)</param>
        /// <param name="day">Día de la consulta (1-31)</param>
        /// <param name="userQuestion">La pregunta del usuario sobre nutrición</param>
        /// <param name="serializedThreadJson">Opcional: JSON del thread existente para continuar conversación</param>
        /// <returns>Respuesta de OpenAI con análisis nutricional basado en los alimentos del día</returns>
        public async Task<NutritionQuestionResult> GetNutritionAnswerAsync(
            string twinId,
            int year,
            int month,
            int day,
            string userQuestion,
            string serializedThreadJson = null)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            if (string.IsNullOrEmpty(userQuestion))
            {
                throw new ArgumentException("User question cannot be null or empty", nameof(userQuestion));
            }

            if (_cosmosService == null)
            {
                throw new InvalidOperationException("AgentNutritionCosmosDB service is not initialized");
            }

            try
            {
                _logger.LogInformation(
                    "?? Obteniendo alimentos para nutrición. TwinID: {TwinID}, Fecha: {Date}",
                    twinId, $"{day}/{month}/{year}");

                // Obtener los alimentos consumidos en el día especificado
                var foodEntries = await _cosmosService.GetFoodDiaryEntriesByDateAndTimeAsync(
                    twinId, year, month, day);

                _logger.LogInformation(
                    "? Se obtuvieron {Count} alimentos para el día {Date}",
                    foodEntries.Count, $"{day}/{month}/{year}");

                // Calcular los totales nutricionales de todos los alimentos del día
                var nutritionTotals = CalculateNutritionTotals(foodEntries);

                // Crear el prompt con los datos de nutrición
                string nutritionContext = CreateNutritionContext(foodEntries, nutritionTotals, userQuestion);

                // Crear agente OpenAI especializado en nutrición
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateNutritionInstructions(),
                    name: "NutritionExpert");

                AgentThread thread;
                
                // Decidir si crear nuevo thread o usar existente
                if (!string.IsNullOrEmpty(serializedThreadJson) && serializedThreadJson != "null")
                {
                    try
                    {
                        // Limpiar y actualizar el thread JSON antes de deserializarlo
                        string cleanedThreadJson = CleanAndUpdateThreadJson(serializedThreadJson, userQuestion);
                        System.Text.Json.JsonElement reloaded = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(cleanedThreadJson, System.Text.Json.JsonSerializerOptions.Web);
                        thread = agent.DeserializeThread(reloaded, System.Text.Json.JsonSerializerOptions.Web);
                        _logger.LogInformation("?? Usando thread existente limpio para TwinID: {TwinID}", twinId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error procesando thread existente. Creando nuevo thread.");
                        thread = agent.GetNewThread();
                    }
                }
                else
                {
                    thread = agent.GetNewThread();
                    _logger.LogInformation("?? Creando nuevo thread para TwinID: {TwinID}", twinId);
                }

                // Ejecutar el agente con el contexto nutricional
                var response = await agent.RunAsync(nutritionContext, thread);
                string aiResponse = response.Text ?? "";

                // Serializar el estado del thread para futuras conversaciones
                string newSerializedJson = thread.Serialize(JsonSerializerOptions.Web).GetRawText();

                // Limpiar y actualizar el JSON del thread
                newSerializedJson = CleanAndUpdateThreadJson(newSerializedJson, userQuestion);

                _logger.LogInformation("? Respuesta de OpenAI generada exitosamente para TwinID: {TwinID}", twinId);

                return new NutritionQuestionResult
                {
                    Success = true,
                    TwinId = twinId,
                    QueryDate = new DateTime(year, month, day),
                    UserQuestion = userQuestion,
                    FoodEntriesCount = foodEntries.Count,
                    NutritionTotals = nutritionTotals,
                    AIResponse = aiResponse,
                    SerializedThreadJson = newSerializedJson,
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "? Error obteniendo respuesta de nutrición. TwinID: {TwinID}, Fecha: {Date}",
                    twinId, $"{day}/{month}/{year}");

                return new NutritionQuestionResult
                {
                    Success = false,
                    ErrorMessage = $"Error procesando pregunta de nutrición: {ex.Message}",
                    TwinId = twinId,
                    QueryDate = new DateTime(year, month, day),
                    UserQuestion = userQuestion,
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Calcula los totales nutricionales sumando todos los atributos de los alimentos del día
        /// </summary>
        private Dictionary<string, double> CalculateNutritionTotals(List<FoodDiaryEntry> foodEntries)
        {
            var totals = new Dictionary<string, double>();

            // Inicializar con los atributos conocidos
            var nutritionAttributes = new[]
            {
                "caloriesPerTypicalServing", "proteinas", "carbohidratos", "grasas",
                "grasasSaturadas", "grasasMonoinsaturadas", "grasasPoliinsaturadas",
                "colesterol", "fibra", "azucares", "sodio", "potasio", "calcio",
                "hierro", "vitaminaA", "vitaminaC", "vitaminaD", "vitaminaE",
                "vitaminaK", "tiamina", "riboflavina", "niacina", "vitaminaB6",
                "folato", "vitaminaB12", "magnesio", "fosforo", "zinc", "cobre",
                "manganeso", "selenio"
            };

            foreach (var attr in nutritionAttributes)
            {
                totals[attr] = 0;
            }

            // Sumar los valores de cada alimento
            foreach (var foodEntry in foodEntries)
            {
                if (foodEntry?.FoodStats?.AtributosComida != null)
                {
                    foreach (var atributo in foodEntry.FoodStats.AtributosComida)
                    {
                        if (atributo?.Nombre != null && nutritionAttributes.Contains(atributo.Nombre))
                        {
                            if (atributo.Value is double doubleValue)
                            {
                                totals[atributo.Nombre] += doubleValue;
                            }
                            else if (atributo.Value is int intValue)
                            {
                                totals[atributo.Nombre] += intValue;
                            }
                            else if (double.TryParse(atributo.Value?.ToString() ?? "", out var parsedValue))
                            {
                                totals[atributo.Nombre] += parsedValue;
                            }
                        }
                    }
                }
            }

            return totals;
        }

        /// <summary>
        /// Crea el contexto nutricional para el prompt de OpenAI
        /// </summary>
        private string CreateNutritionContext(
            List<FoodDiaryEntry> foodEntries,
            Dictionary<string, double> nutritionTotals,
            string userQuestion)
        {
            StringBuilder context = new StringBuilder();

            context.AppendLine("?? CONTEXTO NUTRICIONAL DEL DÍA:");
            context.AppendLine();

            if (foodEntries.Count == 0)
            {
                context.AppendLine("?? No se encontraron alimentos registrados para esta fecha.");
            }
            else
            {
                context.AppendLine($"?? ALIMENTOS CONSUMIDOS ({foodEntries.Count} registros):");
                context.AppendLine();

                foreach (var entry in foodEntries)
                {
                    context.AppendLine($"- {entry.DiaryFood} (Hora: {entry.Time})");
                    if (entry.FoodStats != null)
                    {
                        context.AppendLine($"  Categoría: {entry.FoodStats.Categoria}");
                        if (entry.FoodStats.AtributosComida != null)
                        {
                            var calorias = entry.FoodStats.AtributosComida
                                .FirstOrDefault(a => a.Nombre == "caloriesPerTypicalServing")?.Value;
                            if (calorias != null)
                            {
                                context.AppendLine($"  Calorías: {calorias}");
                            }
                        }
                    }
                }
            }

            // Construir resumen nutricional en una sola línea eficiente
            var mainNutrients = new[] { "caloriesPerTypicalServing", "proteinas", "carbohidratos", "grasas", "fibra", "azucares", "sodio", "calcio", "hierro" };
            var nutritionLines = mainNutrients.Where(n => nutritionTotals.ContainsKey(n) && nutritionTotals[n] > 0).Select(n => $"  • {GetNutrientLabel(n)}: {nutritionTotals[n]:F2}g").ToList();
            string nutritionSummary = nutritionLines.Any() ? $"?? TOTALES NUTRICIONALES DEL DÍA:\n{string.Join("\n", nutritionLines)}" : "";

            // Construcción del prompt completo con instrucciones HTML detalladas
            context.Append($"\n{nutritionSummary}\n\n? PREGUNTA DEL USUARIO: {userQuestion}\n\n");
            context.Append("?? IMPORTANTE: SIEMPRE RESPONDE si la pregunta es sobre nutrición\n\n");
            context.Append("ANÁLISIS DE CONTEXTO:\n");
            context.Append("- Revisa el HISTORIAL COMPLETO de conversación anterior (si existe)\n");
            context.Append("- Entiende el tema general de la conversación\n");
            context.Append("- Mantén coherencia con las respuestas previas\n");
            context.Append("- Si el usuario pregunta algo relacionado con nutrición, CONTESTA\n");
            context.Append("- NO rechaces preguntas válidas sobre alimentos, dietas o nutrición\n\n");
            context.Append("METODOLOGÍA: CHAIN OF THOUGHT\n");
            context.Append("1. ANALIZA: ¿Qué pregunta el usuario? ¿Es sobre nutrición?\n");
            context.Append("2. CONTEXTO: ¿Qué dijimos antes? ¿Cuál es el hilo de la conversación?\n");
            context.Append("3. RAZONA: ¿Cómo se relaciona con los datos que tengo?\n");
            context.Append("4. RESPONDE: Proporciona respuesta coherente y útil\n\n");
            context.Append("REGLAS DE RESPUESTA:\n");
            context.Append("- Si pregunta sobre nutrición, SIEMPRE CONTESTA\n");
            context.Append("- Usa información del historial para dar contexto\n");
            context.Append("- Sé conversacional y natural\n");
            context.Append("- NO rechaces preguntas válidas\n");
            context.Append("- Responde de forma HTML minimalista (solo estructura básica)\n\n");

            context.Append("RECOMENDACIONES PARA RESPONDER:\n");
            context.Append("- Asegúrate de que la respuesta sea clara y fácil de entender\n");
            context.Append("- No des información contradictoria o confusa\n");
            context.Append("- Si no conoces la respuesta, es mejor admitirlo que dar información incorrecta\n");
            context.Append("- Siempre que sea posible, ofrece recomendaciones prácticas y basadas en evidencia\n");
            context.Append("- Evita terminología técnica sin explicación, usa un lenguaje sencillo\n");
            context.Append("- La empatía y el apoyo son clave; motiva al usuario a mantener hábitos saludables\n");
            context.Append("- Considera las preocupaciones culturales y personales del usuario en tus respuestas\n\n");

            //context.Append("ESTRUCTURA RECOMENDADA (personaliza siempre el contenido):\n");
            //context.Append("<!DOCTYPE html>\n<html lang=\"es\">\n<head>\n");
            //context.Append("  <meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
            //context.Append("  <style>\n");
            //context.Append("    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%); margin: 0; padding: 20px; }\n");
            //context.Append("    .container { max-width: 900px; margin: 0 auto; background: white; border-radius: 15px; box-shadow: 0 10px 40px rgba(0,0,0,0.15); padding: 40px; }\n");
            //context.Append("    h1 { color: #1a365d; border-bottom: 4px solid #3182ce; padding-bottom: 15px; font-size: 24px; margin-bottom: 20px; }\n");
            //context.Append("    .content { color: #2d3748; line-height: 1.8; font-size: 16px; }\n");
            //context.Append("    .highlight { background: linear-gradient(135deg, #cfe2ff 0%, #b6d4fe 100%); border-left: 5px solid #0d6efd; padding: 15px; border-radius: 8px; margin: 15px 0; }\n");
            //context.Append("    .success { background: linear-gradient(135deg, #d4edda 0%, #c3e6cb 100%); border-left: 5px solid #28a745; padding: 15px; border-radius: 8px; margin: 15px 0; }\n");
            //context.Append("    .warning { background: linear-gradient(135deg, #fef5e7 0%, #f8d7a1 100%); border-left: 5px solid #f39c12; padding: 15px; border-radius: 8px; margin: 15px 0; }\n");
            //context.Append("    ul { list-style: none; padding: 0; margin: 10px 0; }\n");
            //context.Append("    li { padding: 8px 0; padding-left: 25px; position: relative; }\n");
            //context.Append("    li:before { content: '?'; position: absolute; left: 0; color: #3182ce; font-weight: bold; }\n");
            //context.Append("    .footer { margin-top: 30px; padding-top: 20px; border-top: 2px solid #e2e8f0; text-align: center; color: #718096; font-size: 13px; font-style: italic; }\n");
            //context.Append("  </style>\n</head>\n<body>\n");
            //context.Append("  <div class=\"container\">\n");
            //context.Append("    <h1>?? Tu Respuesta</h1>\n");
            //context.Append("    <div class=\"content\">\n");
            //context.Append("      [RESPUESTA DIRECTA Y CLARA A LA PREGUNTA DEL USUARIO]\n");
            //context.Append("    </div>\n");
            //context.Append("    <div class=\"highlight\">\n");
            //context.Append("      <strong>?? Consejo Práctico:</strong><br>[Recomendación basada en los datos nutricionales si es relevante]\n");
            //context.Append("    </div>\n");
            //context.Append("    <div class=\"footer\">Cuéntame si tienes más preguntas sobre tu nutrición. Estoy aquí para ayudarte. ??</div>\n");
            //context.Append("  </div>\n</body>\n</html>\n\n");
            context.Append("INSTRUCCIONES FINALES:\n");
            context.Append("- Responde DIRECTAMENTE la pregunta del usuario\n");
            context.Append("- NO repitas datos que ya tiene (alimentos, calorías, etc.)\n");
            context.Append("- Sé conciso y al punto\n");
            context.Append("- Si necesitas referencias numéricas, usa solo los números relevantes a su pregunta\n");
            context.Append("- Personaliza tu respuesta según lo que preguntó, no según lo que consumió\n");
            context.Append("- Usa el HTML solo para estructura básica, nada complejo\n");

            return context.ToString();
        }

        /// <summary>
        /// Obtiene la etiqueta legible de un atributo nutricional
        /// </summary>
        private string GetNutrientLabel(string nutrientName)
        {
            return nutrientName switch
            {
                "caloriesPerTypicalServing" => "Calorías",
                "proteinas" => "Proteínas",
                "carbohidratos" => "Carbohidratos",
                "grasas" => "Grasas",
                "grasasSaturadas" => "Grasas Saturadas",
                "fibra" => "Fibra",
                "azucares" => "Azúcares",
                "sodio" => "Sodio",
                "calcio" => "Calcio",
                "hierro" => "Hierro",
                "vitaminaA" => "Vitamina A",
                "vitaminaC" => "Vitamina C",
                "vitaminaD" => "Vitamina D",
                "vitaminaE" => "Vitamina E",
                "potasio" => "Potasio",
                "magnesio" => "Magnesio",
                "zinc" => "Zinc",
                _ => nutrientName
            };
        }

        /// <summary>
        /// Crea las instrucciones para el agente especializado en nutrición
        /// </summary>
        private string CreateNutritionInstructions()
        {
            return @"
?? IDENTITY: Eres un EXPERTO EN NUTRICIÓN dedicado a ayudar usuarios con preguntas sobre alimentación.

?? TU OBJETIVO:
- Responder TODAS las preguntas válidas sobre nutrición, dietas y alimentos
- Mantener un diálogo fluido y coherente
- Analizar el historial de conversación para dar respuestas contextualizadas
- Usar chain of thought para razonar antes de responder

?? REQUISITOS CRÍTICOS:
1. SIEMPRE CONTESTA: Si la pregunta es sobre nutrición, proporciona respuesta
2. NO RECHACES: No digas ""lo siento, no puedo ayudar"" a preguntas válidas
3. CONTEXTO: Lee todo el historial de mensajes previos
4. COHERENCIA: Mantén consistencia con lo que dijiste antes
5. LENGUAJE: Responde en ESPAÑOL claro y accesible

?? METODOLOGÍA - CHAIN OF THOUGHT:
Antes de responder, sigue estos pasos mentales:
1. Identifica la pregunta del usuario
2. Verifica que sea sobre nutrición (si es así, CONTESTA)
3. Busca contexto en el historial previo
4. Razona la respuesta basándote en:
   - Los datos nutricionales disponibles
   - Lo que ya hablamos antes
   - Recomendaciones nutricionales generales
5. Genera respuesta coherente y útil

?? SITUACIONES ESPECIALES:
- Pregunta genérica sobre nutrición ? CONTESTA CON INFORMACIÓN GENERAL
- Pregunta sobre alimentos específicos ? USA DATOS DEL DÍA SI APLICA
- Pregunta fuera de tema ? REDIRIGE CON AMABILIDAD
- Pregunta ambigua ? ACLARA Y LUEGO CONTESTA

?? FORMATO DE RESPUESTA:
- Respuesta directa a la pregunta
- Si es relevante: consejo práctico basado en datos
- Mantén HTML simple (solo estructura)
- Sé conciso pero útil

?? RECUERDA:
- Estás en una CONVERSACIÓN, no en un intercambio aislado
- Cada pregunta puede conectar con la anterior
- El usuario confía en que SIEMPRE intentarás ayudar
- La fluidez conversacional es más importante que la perfección";
        }

        private string CleanAndUpdateThreadJson(string serializedThreadJson, string userQuestion)
        {
            try
            {
                var threadElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(serializedThreadJson, System.Text.Json.JsonSerializerOptions.Web);
                
                if (!threadElement.TryGetProperty("storeState", out var storeState)) return serializedThreadJson;
                if (!storeState.TryGetProperty("messages", out var messages)) return serializedThreadJson;

                var messageList = messages.EnumerateArray().ToList();
                var cleanedMessages = new System.Collections.Generic.List<System.Text.Json.JsonElement>();

                for (int i = 0; i < messageList.Count; i++)
                {
                    var msg = messageList[i];
                    
                    if (!msg.TryGetProperty("role", out var roleElement)) continue;
                    var role = roleElement.GetString();

                    var cleanMsg = new System.Collections.Generic.Dictionary<string, object>();
                    
                    if (msg.TryGetProperty("role", out var prop)) cleanMsg["role"] = role;
                    if (msg.TryGetProperty("authorName", out var authorProp)) cleanMsg["authorName"] = authorProp.GetString();
                    if (msg.TryGetProperty("createdAt", out var createdProp)) cleanMsg["createdAt"] = createdProp.GetString();
                    if (msg.TryGetProperty("messageId", out var msgIdProp)) cleanMsg["messageId"] = msgIdProp.GetString();

                    if (msg.TryGetProperty("contents", out var contentsProp))
                    {
                        var contentsList = new System.Collections.Generic.List<object>();
                        
                        foreach (var content in contentsProp.EnumerateArray())
                        {
                            if (content.TryGetProperty("text", out var textProp))
                            {
                                var originalText = textProp.GetString() ?? "";
                                string cleanedText = originalText;

                                if (role == "user")
                                {
                                    cleanedText = ExtractUserQuestion(originalText);
                                }
                                else if (role == "assistant")
                                {
                                    cleanedText = ConvertHtmlToPlainText(originalText);
                                }

                                var contentObj = new System.Collections.Generic.Dictionary<string, object>();
                                if (content.TryGetProperty("$type", out var typeProp)) 
                                    contentObj["$type"] = typeProp.GetString();
                                contentObj["text"] = cleanedText;
                                
                                contentsList.Add(contentObj);
                            }
                        }
                        
                        cleanMsg["contents"] = contentsList;
                    }

                    cleanedMessages.Add(System.Text.Json.JsonSerializer.SerializeToElement(cleanMsg, System.Text.Json.JsonSerializerOptions.Web));
                }

                var cleanedThread = new System.Collections.Generic.Dictionary<string, object>();
                var cleanedStoreState = new System.Collections.Generic.Dictionary<string, object>();
                cleanedStoreState["messages"] = cleanedMessages;
                cleanedThread["storeState"] = cleanedStoreState;

                return System.Text.Json.JsonSerializer.Serialize(cleanedThread, System.Text.Json.JsonSerializerOptions.Web);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "?? Error limpiando thread JSON. Retornando original.");
                return serializedThreadJson;
            }
        }

        private string ExtractUserQuestion(string userMessage)
        {
            if (userMessage.Contains("? PREGUNTA DEL USUARIO:"))
            {
                var preguntaIndex = userMessage.IndexOf("? PREGUNTA DEL USUARIO:");
                if (preguntaIndex >= 0)
                {
                    var questionPart = userMessage.Substring(preguntaIndex);
                    var endIndex = questionPart.IndexOf("\n\n");
                    if (endIndex > 0)
                    {
                        return questionPart.Substring(0, endIndex).Replace("? PREGUNTA DEL USUARIO:", "").Trim();
                    }
                    return questionPart.Replace("? PREGUNTA DEL USUARIO:", "").Trim();
                }
            }

            if (userMessage.Contains("?? CONTEXTO NUTRICIONAL"))
            {
                var lines = userMessage.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
                var questionLine = lines.FirstOrDefault(l => 
                    !l.Contains("??") && 
                    !l.Contains("??") && 
                    !l.Contains("??") && 
                    !l.Contains("CONTEXTO") &&
                    !l.Contains("ALIMENTOS") &&
                    !l.Contains("TOTALES") &&
                    !l.Contains("Categoría") &&
                    !l.Contains("Calorías") &&
                    !l.Contains("Hora:") &&
                    !string.IsNullOrWhiteSpace(l) &&
                    l.Length > 5);
                
                if (!string.IsNullOrEmpty(questionLine))
                    return questionLine.Trim();
            }

            return userMessage.Trim();
        }

        private string ConvertHtmlToPlainText(string htmlContent)
        {
            if (!htmlContent.Contains("<html") && !htmlContent.Contains("<body"))
            {
                return htmlContent;
            }

            try
            {
                var text = System.Text.RegularExpressions.Regex.Replace(htmlContent, @"<!DOCTYPE[^>]*>", "");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"<\?xml[^>]*\?>", "");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"<script[^>]*>.*?</script>", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"<style[^>]*>.*?</style>", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]*>", "");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"&nbsp;", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"&lt;", "<");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"&gt;", ">");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"&amp;", "&");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"&quot;", "\"");
                
                return text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "?? Error convirtiendo HTML a texto. Retornando original.");
                return htmlContent;
            }
        }
    }

    #region Data Models

    public class NutritionQuestionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public DateTime QueryDate { get; set; }
        public string UserQuestion { get; set; } = "";
        public int FoodEntriesCount { get; set; }
        public Dictionary<string, double> NutritionTotals { get; set; } = new();
        public string AIResponse { get; set; } = "";
        public string SerializedThreadJson { get; set; } = "";
        public DateTime ProcessedTimestamp { get; set; }
    }

    #endregion
}
