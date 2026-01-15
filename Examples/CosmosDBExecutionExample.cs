using System;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Complete example showing AI-powered SQL generation and execution against Cosmos DB
    /// </summary>
    public class CosmosDBExecutionExample
    {
        public static async Task RunCompleteExample()
        {
            try
            {
                var cosmosAgent = new AgentCosmosDB();

                // Define the family database schema
                string familySchema = @"
Schema Fields:
[""id""] = Id,
[""TwinID""] = TwinID,
[""parentesco""] = Parentesco (relationship: Padre, Madre, Hermano, Hermana, Hijo, Hija, etc.),
[""nombre""] = Nombre (first name),
[""apellido""] = Apellido (last name),
[""email""] = Email,
[""telefono""] = Telefono,
[""fecha_nacimiento""] = FechaNacimiento,
[""nombre_twin""] = NombreTwin,
[""direccion_completa""] = DireccionCompleta,
[""pais_nacimiento""] = PaisNacimiento,
[""nacionalidad""] = Nacionalidad,
[""genero""] = Genero,
[""ocupacion""] = Ocupacion,
[""intereses""] = Intereses,
[""idiomas""] = Idiomas,
[""numero_celular""] = NumeroCelular,
[""url_foto""] = UrlFoto,
[""notas""] = Notas,
[""createdDate""] = CreatedDate,
[""type""] = Type";

                string twinId = "388a31e7-d408-40f0-844c-4d2efedaa836";
                string containerName = "FamilyMembers";

                Console.WriteLine("=== COSMOS DB AI-POWERED SQL EXECUTION EXAMPLES ===\n");

                // Example 1: Complete AI workflow - Generate and Execute
                Console.WriteLine("?? Example 1: AI-Generated Query with Execution");
                Console.WriteLine("Question: Find all parents in the family");
                string result1 = await cosmosAgent.AgentBuildAndExecuteSQL(
                    familySchema,
                    containerName,
                    "Find all parents in the family",
                    twinId
                );
                Console.WriteLine("Result:");
                Console.WriteLine(result1);
                Console.WriteLine("\n" + "=".PadRight(80, '=') + "\n");

                // Example 2: Two-step process - Generate then Execute
                Console.WriteLine("?? Example 2: Two-Step Process (Generate + Execute)");
                Console.WriteLine("Question: Find family members born in Mexico");
                
                // Step 1: Generate SQL
                string sql2 = await cosmosAgent.AgentBuildSQL(
                    familySchema,
                    containerName,
                    "Find family members born in Mexico",
                    twinId
                );
                Console.WriteLine($"Generated SQL: {sql2}");
                
                // Step 2: Execute SQL
                string result2 = await cosmosAgent.ExecuteSQLQuery(sql2, containerName, twinId);
                Console.WriteLine("Execution Result:");
                Console.WriteLine(result2);
                Console.WriteLine("\n" + "=".PadRight(80, '=') + "\n");

                // Example 3: Complex query with multiple conditions
                Console.WriteLine("?? Example 3: Complex Query with Multiple Conditions");
                Console.WriteLine("Question: Find female family members who are doctors");
                string result3 = await cosmosAgent.AgentBuildAndExecuteSQL(
                    familySchema,
                    containerName,
                    "Find female family members who are doctors",
                    twinId
                );
                Console.WriteLine("Result:");
                Console.WriteLine(result3);
                Console.WriteLine("\n" + "=".PadRight(80, '=') + "\n");

                // Example 4: Direct SQL execution (pre-written query)
                Console.WriteLine("?? Example 4: Direct SQL Execution");
                string directSQL = $"SELECT c.nombre, c.apellido, c.parentesco, c.ocupacion FROM {containerName} c WHERE c.TwinID = '{twinId}' AND c.genero = 'Femenino'";
                Console.WriteLine($"Direct SQL: {directSQL}");
                string result4 = await cosmosAgent.ExecuteSQLQuery(directSQL, containerName, twinId);
                Console.WriteLine("Result:");
                Console.WriteLine(result4);
                Console.WriteLine("\n" + "=".PadRight(80, '=') + "\n");

                // Example 5: Error handling demonstration
                Console.WriteLine("?? Example 5: Error Handling - Invalid Query");
                try
                {
                    string invalidSQL = "INVALID SQL QUERY";
                    string result5 = await cosmosAgent.ExecuteSQLQuery(invalidSQL, containerName, twinId);
                    Console.WriteLine("Result:");
                    Console.WriteLine(result5);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Expected error handled: {ex.Message}");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error in complete example: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Service class demonstrating how to integrate AgentCosmosDB in a real application
    /// </summary>
    public class FamilySearchService
    {
        private readonly AgentCosmosDB _cosmosAgent;
        private readonly string _familySchema;
        private readonly string _containerName;

        public FamilySearchService()
        {
            _cosmosAgent = new AgentCosmosDB();
            _containerName = "FamilyMembers";
            _familySchema = @"
Schema Fields:
[""id""] = Id,
[""TwinID""] = TwinID,
[""parentesco""] = Parentesco (relationship: Padre, Madre, Hermano, Hermana, etc.),
[""nombre""] = Nombre (first name),
[""apellido""] = Apellido (last name),
[""email""] = Email,
[""telefono""] = Telefono,
[""fecha_nacimiento""] = FechaNacimiento,
[""ocupacion""] = Ocupacion,
[""genero""] = Genero";
        }

        /// <summary>
        /// Search family members using natural language
        /// </summary>
        public async Task<string> SearchFamilyAsync(string twinId, string naturalLanguageQuery)
        {
            try
            {
                Console.WriteLine($"?? Searching for: {naturalLanguageQuery}");
                Console.WriteLine($"?? Twin ID: {twinId}");

                string results = await _cosmosAgent.AgentBuildAndExecuteSQL(
                    _familySchema,
                    _containerName,
                    naturalLanguageQuery,
                    twinId
                );

                return results;
            }
            catch (Exception ex)
            {
                string errorMessage = $"Search failed: {ex.Message}";
                Console.WriteLine($"? {errorMessage}");
                return errorMessage;
            }
        }

        /// <summary>
        /// Get specific family relationships
        /// </summary>
        public async Task<string> GetFamilyRelationshipsAsync(string twinId, string relationship)
        {
            string query = $"Find all family members with relationship {relationship}";
            return await SearchFamilyAsync(twinId, query);
        }

        /// <summary>
        /// Search by occupation
        /// </summary>
        public async Task<string> SearchByOccupationAsync(string twinId, string occupation)
        {
            string query = $"Find family members whose occupation is {occupation}";
            return await SearchFamilyAsync(twinId, query);
        }

        /// <summary>
        /// Search by location/country
        /// </summary>
        public async Task<string> SearchByCountryAsync(string twinId, string country)
        {
            string query = $"Find family members born in {country}";
            return await SearchFamilyAsync(twinId, query);
        }
    }

    /// <summary>
    /// Console application to test the complete Cosmos DB execution functionality
    /// </summary>
    public class CosmosDBExecutionTestProgram
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== COSMOS DB SQL EXECUTION TESTING ===\n");

            // Run the complete example
            await CosmosDBExecutionExample.RunCompleteExample();

            Console.WriteLine("\n=== FAMILY SEARCH SERVICE DEMO ===\n");

            // Demonstrate the service class
            var familyService = new FamilySearchService();
            string testTwinId = "388a31e7-d408-40f0-844c-4d2efedaa836";

            try
            {
                // Test different search scenarios
                Console.WriteLine("?? Searching for parents:");
                string parents = await familyService.GetFamilyRelationshipsAsync(testTwinId, "Padre");
                Console.WriteLine(parents);

                Console.WriteLine("\n?? Searching for doctors:");
                string doctors = await familyService.SearchByOccupationAsync(testTwinId, "doctor");
                Console.WriteLine(doctors);

                Console.WriteLine("\n?? Searching for family members born in Mexico:");
                string mexicanFamily = await familyService.SearchByCountryAsync(testTwinId, "Mexico");
                Console.WriteLine(mexicanFamily);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Service demo error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}