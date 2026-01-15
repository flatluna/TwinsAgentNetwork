using System;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Services;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Ejemplos prácticos de cómo usar el nuevo método GetNutritionAnswerAsync
    /// para hacer preguntas sobre nutrición basadas en los alimentos consumidos en un día
    /// </summary>
    public class NutritionQuestionExample
    {
        private readonly AgentTwinFoodDietery _foodDieteryAgent;
        private readonly ILogger<NutritionQuestionExample> _logger;

        public NutritionQuestionExample(
            AgentTwinFoodDietery foodDieteryAgent,
            ILogger<NutritionQuestionExample> logger)
        {
            _foodDieteryAgent = foodDieteryAgent;
            _logger = logger;
        }

        /// <summary>
        /// Ejemplo 1: Pregunta simple sobre si fue saludable la alimentación
        /// </summary>
        public async Task Example1_SimpleHealthQuestion()
        {
            _logger.LogInformation("?? Ejemplo 1: Pregunta simple sobre nutrición");

            try
            {
                var result = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "¿Fue saludable lo que comí hoy?");

                if (result.Success)
                {
                    Console.WriteLine("? Respuesta recibida:");
                    Console.WriteLine($"   Alimentos: {result.FoodEntriesCount}");
                    Console.WriteLine($"   Calorías totales: {result.NutritionTotals["caloriesPerTypicalServing"]:F0}");
                    Console.WriteLine($"   Respuesta:\n{result.AIResponse}");
                }
                else
                {
                    Console.WriteLine($"? Error: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Ejemplo 1");
            }
        }

        /// <summary>
        /// Ejemplo 2: Análisis de déficit de nutrientes
        /// </summary>
        public async Task Example2_NutrientDeficiencyAnalysis()
        {
            _logger.LogInformation("?? Ejemplo 2: Análisis de déficit de nutrientes");

            try
            {
                var result = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "¿Qué nutrientes me faltaron hoy? ¿Debo mejorar algo?");

                if (result.Success)
                {
                    Console.WriteLine("? Análisis de nutrientes:");
                    Console.WriteLine($"   Proteínas: {result.NutritionTotals["proteinas"]:F1}g");
                    Console.WriteLine($"   Carbohidratos: {result.NutritionTotals["carbohidratos"]:F1}g");
                    Console.WriteLine($"   Grasas: {result.NutritionTotals["grasas"]:F1}g");
                    Console.WriteLine($"   Fibra: {result.NutritionTotals["fibra"]:F1}g");
                    Console.WriteLine($"\n   Recomendación:\n{result.AIResponse}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Ejemplo 2");
            }
        }

        /// <summary>
        /// Ejemplo 3: Consulta sobre distribución de macronutrientes
        /// </summary>
        public async Task Example3_MacronutrientDistribution()
        {
            _logger.LogInformation("?? Ejemplo 3: Distribución de macronutrientes");

            try
            {
                var result = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "¿Cuál fue la proporción de proteínas, grasas y carbohidratos en mi dieta?");

                if (result.Success)
                {
                    var totalCals = result.NutritionTotals["caloriesPerTypicalServing"];
                    var protein = result.NutritionTotals["proteinas"];
                    var carbs = result.NutritionTotals["carbohidratos"];
                    var fat = result.NutritionTotals["grasas"];

                    // Calcular porcentajes (4 cal/g para proteína y carbohidratos, 9 cal/g para grasa)
                    double proteinCals = protein * 4;
                    double carbsCals = carbs * 4;
                    double fatCals = fat * 9;

                    Console.WriteLine("? Distribución de macronutrientes:");
                    Console.WriteLine($"   Proteína: {(proteinCals / totalCals * 100):F1}%");
                    Console.WriteLine($"   Carbohidratos: {(carbsCals / totalCals * 100):F1}%");
                    Console.WriteLine($"   Grasas: {(fatCals / totalCals * 100):F1}%");
                    Console.WriteLine($"\n   Análisis:\n{result.AIResponse}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Ejemplo 3");
            }
        }

        /// <summary>
        /// Ejemplo 4: Conversación continua usando threads
        /// Demuestra cómo mantener contexto en múltiples preguntas
        /// </summary>
        public async Task Example4_ContinuousConversation()
        {
            _logger.LogInformation("?? Ejemplo 4: Conversación continua con threads");

            try
            {
                string threadJson = null;

                // Pregunta 1
                Console.WriteLine("\n1?? Primera pregunta:");
                var result1 = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "¿Cómo fue mi alimentación hoy?");

                if (result1.Success)
                {
                    Console.WriteLine($"   Respuesta: {result1.AIResponse.Substring(0, Math.Min(100, result1.AIResponse.Length))}...");
                    threadJson = result1.SerializedThreadJson;
                }

                // Pregunta 2 - usando el thread anterior
                Console.WriteLine("\n2?? Segunda pregunta (con contexto):");
                var result2 = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "¿Qué debería cambiar para una dieta más equilibrada?",
                    serializedThreadJson: threadJson);

                if (result2.Success)
                {
                    Console.WriteLine($"   Respuesta: {result2.AIResponse.Substring(0, Math.Min(100, result2.AIResponse.Length))}...");
                    threadJson = result2.SerializedThreadJson;
                }

                // Pregunta 3 - continuando la conversación
                Console.WriteLine("\n3?? Tercera pregunta (continuación):");
                var result3 = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "¿Cuáles son las vitaminas más importantes que debería priorizar?",
                    serializedThreadJson: threadJson);

                if (result3.Success)
                {
                    Console.WriteLine($"   Respuesta: {result3.AIResponse.Substring(0, Math.Min(100, result3.AIResponse.Length))}...");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Ejemplo 4");
            }
        }

        /// <summary>
        /// Ejemplo 5: Análisis detallado de micronutrientes
        /// </summary>
        public async Task Example5_MicronutrientAnalysis()
        {
            _logger.LogInformation("?? Ejemplo 5: Análisis de micronutrientes");

            try
            {
                var result = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "¿Consumí suficientes vitaminas y minerales? ¿Calcio, hierro, vitamina C?");

                if (result.Success)
                {
                    Console.WriteLine("? Análisis de micronutrientes:");
                    
                    var micronutrients = new[]
                    {
                        ("Calcio", "calcio"),
                        ("Hierro", "hierro"),
                        ("Vitamina C", "vitaminaC"),
                        ("Sodio", "sodio"),
                        ("Potasio", "potasio"),
                        ("Zinc", "zinc")
                    };

                    foreach (var (label, key) in micronutrients)
                    {
                        if (result.NutritionTotals.ContainsKey(key) && result.NutritionTotals[key] > 0)
                        {
                            Console.WriteLine($"   {label}: {result.NutritionTotals[key]:F1}");
                        }
                    }

                    Console.WriteLine($"\n   Recomendación:\n{result.AIResponse}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Ejemplo 5");
            }
        }

        /// <summary>
        /// Ejemplo 6: Simulación de chat interactivo
        /// Demuestra cómo podría funcionar en una aplicación interactiva
        /// </summary>
        public async Task Example6_InteractiveChat()
        {
            _logger.LogInformation("?? Ejemplo 6: Chat interactivo");

            try
            {
                string threadJson = null;
                var twinId = "user-12345";
                var year = 2025;
                var month = 1;
                var day = 15;

                // Preguntas de ejemplo que podría hacer un usuario
                var questions = new[]
                {
                    "¿Cómo estuvo mi alimentación hoy?",
                    "¿Comí muchas grasas saturadas?",
                    "¿Mi fibra fue suficiente?",
                    "¿Qué recomendaciones tienes para mañana?"
                };

                foreach (var question in questions)
                {
                    Console.WriteLine($"\n?? Usuario: {question}");

                    var result = await _foodDieteryAgent.GetNutritionAnswerAsync(
                        twinId: twinId,
                        year: year,
                        month: month,
                        day: day,
                        userQuestion: question,
                        serializedThreadJson: threadJson);

                    if (result.Success)
                    {
                        Console.WriteLine($"?? Asesor nutricional:\n{result.AIResponse}");
                        threadJson = result.SerializedThreadJson;
                    }
                    else
                    {
                        Console.WriteLine($"? Error: {result.ErrorMessage}");
                    }

                    // Pequeña pausa entre preguntas
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Ejemplo 6");
            }
        }

        /// <summary>
        /// Ejemplo 7: Comparación con día anterior (usando dos llamadas)
        /// </summary>
        public async Task Example7_DayComparison()
        {
            _logger.LogInformation("?? Ejemplo 7: Comparación entre dos días");

            try
            {
                // Obtener datos del día 14
                var day14 = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 14,
                    userQuestion: "Resumen nutricional del día");

                // Obtener datos del día 15
                var day15 = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    twinId: "user-12345",
                    year: 2025,
                    month: 1,
                    day: 15,
                    userQuestion: "Resumen nutricional del día");

                if (day14.Success && day15.Success)
                {
                    Console.WriteLine("?? Comparación 14-15 de Enero:");
                    Console.WriteLine($"\n   Día 14:");
                    Console.WriteLine($"   - Alimentos: {day14.FoodEntriesCount}");
                    Console.WriteLine($"   - Calorías: {day14.NutritionTotals["caloriesPerTypicalServing"]:F0}");
                    Console.WriteLine($"   - Proteínas: {day14.NutritionTotals["proteinas"]:F1}g");

                    Console.WriteLine($"\n   Día 15:");
                    Console.WriteLine($"   - Alimentos: {day15.FoodEntriesCount}");
                    Console.WriteLine($"   - Calorías: {day15.NutritionTotals["caloriesPerTypicalServing"]:F0}");
                    Console.WriteLine($"   - Proteínas: {day15.NutritionTotals["proteinas"]:F1}g");

                    var calDiff = day15.NutritionTotals["caloriesPerTypicalServing"] -
                                 day14.NutritionTotals["caloriesPerTypicalServing"];
                    Console.WriteLine($"\n   Diferencia de calorías: {(calDiff > 0 ? "+" : "")}{calDiff:F0}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Ejemplo 7");
            }
        }
    }
}
