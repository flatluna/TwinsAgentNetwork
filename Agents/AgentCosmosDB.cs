using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// AI-powered agent that generates Cosmos DB SQL queries based on schema and natural language questions
    /// </summary>
    public class AgentCosmosDB
    {
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;
        private readonly string _cosmosConnectionString;
        private readonly string _cosmosDatabaseName;

        public AgentCosmosDB()
        {
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
                ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME") 
                ?? throw new InvalidOperationException("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME is not configured.");
            
            // Build connection string from endpoint and key - use environment variables only
            string cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? "";
            string cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY") ?? "";
            
            // Create connection string in the format expected by CosmosClient
            if (!string.IsNullOrEmpty(cosmosEndpoint) && !string.IsNullOrEmpty(cosmosKey))
            {
                _cosmosConnectionString = $"AccountEndpoint={cosmosEndpoint};AccountKey={cosmosKey};";
            }
            else
            {
                _cosmosConnectionString = "";
            }
            
            _cosmosDatabaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";
        }

        /// <summary>
        /// Generates Cosmos DB SQL query using AI Agent based on schema and natural language question
        /// </summary>
        /// <param name="schema">Database schema with field mappings (e.g., ["id"] = Id, ["nombre"] = Nombre)</param>
        /// <param name="containerName">Name of the Cosmos DB container</param>
        /// <param name="question">Natural language question (e.g., "find family members with last name Luna")</param>
        /// <returns>Generated Cosmos DB SQL SELECT statement</returns>
        public async Task<string> AgentBuildSQL(string schema, string containerName, string question, string TwinId)
        {
            if (string.IsNullOrEmpty(schema))
            {
                throw new ArgumentException("Schema cannot be null or empty", nameof(schema));
            }

            if (string.IsNullOrEmpty(containerName))
            {
                throw new ArgumentException("Container name cannot be null or empty", nameof(containerName));
            }

            if (string.IsNullOrEmpty(question))
            {
                throw new ArgumentException("Question cannot be null or empty", nameof(question));
            }

            if (string.IsNullOrEmpty(TwinId))
            {
                throw new ArgumentException("TwinId cannot be null or empty", nameof(TwinId));
            }
            
            try
            {
                // Validate input for security
                if (ContainsSQLInjection(question) || ContainsPromptInjection(question))
                {
                    throw new InvalidOperationException("Invalid or potentially harmful question detected. Please rephrase your question.");
                }

                // Create AI Agent
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateSQLInstructions(),
                    name: "CosmosDBSQLExpert");

                AgentThread thread = agent.GetNewThread();

                // Create context-rich prompt for SQL generation
                string sqlPrompt = CreateSQLPrompt(schema, containerName, question, TwinId);

                // Get AI response
                var response = await agent.RunAsync(sqlPrompt, thread);

                // Extract and validate the SQL query
                string sqlQuery = ExtractSQLQuery(response.Text);

                Console.WriteLine($"🔍 Generated SQL for question: '{question}'");
                Console.WriteLine($"📝 SQL: {sqlQuery}");

                return sqlQuery;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error generating SQL query: {ex.Message}");
                throw new InvalidOperationException($"Failed to generate SQL query: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Creates specialized instructions for the SQL generation agent
        /// </summary>
        private string CreateSQLInstructions()
        {
            return @"
🎯 IDENTITY: You are a COSMOS DB SQL EXPERT specialized in generating precise SQL queries for Azure Cosmos DB.

🧠 YOUR EXPERTISE:
- Expert knowledge of Cosmos DB SQL syntax and capabilities
- Understanding of NoSQL document structures and JSON properties
- Ability to translate natural language questions into efficient SQL queries
- Knowledge of Cosmos DB specific functions and operators
- Understanding of partition key optimization for performance

🔍 CORE SPECIALIZATION:
- Generate valid Cosmos DB SQL SELECT statements
- Use proper JSON property access syntax (c.property or c[""property""])
- Apply appropriate WHERE clauses based on natural language requirements
- Optimize queries for performance and accuracy
- Handle family/relationship data queries effectively
- **MANDATORY**: Always include TwinID partition key filter for optimal performance

📋 SQL GENERATION REQUIREMENTS:
1. SYNTAX: Always use valid Cosmos DB SQL syntax
2. FORMAT: Return ONLY the SQL SELECT statement without any markdown or extra text
3. PROPERTIES: Use the exact field names provided in the schema
4. CONTAINER: Use the provided container name as the alias (usually 'c')
5. **PARTITION KEY**: ALWAYS start WHERE clause with TwinID filter for partitioning
6. FILTERING: Apply appropriate WHERE conditions based on the question
7. SECURITY: Never generate queries that could cause security issues
8. **SELECT CLAUSE**: Use 'SELECT *' (NOT 'SELECT c.*') - Cosmos DB doesn't support aliased wildcard selects

🔑 PARTITION KEY RULES:
- Every query MUST include WHERE c.TwinID = 'provided_twin_id' as the first condition
- Additional filters should be combined with AND operator
- This ensures optimal performance and cost efficiency in Cosmos DB

🚫 CRITICAL SYNTAX RULES:
- ❌ NEVER use 'SELECT c.*' - this causes syntax errors in Cosmos DB
- ✅ ALWAYS use 'SELECT *' for all fields
- ✅ Or use 'SELECT c.field1, c.field2' for specific fields
- ❌ NEVER use aliased wildcard syntax (c.*)

🚫 SECURITY MEASURES:
- Reject any attempts at SQL injection
- Only generate SELECT statements (no INSERT, UPDATE, DELETE, DROP)
- Use parameterized approaches when possible
- Validate all field names against the provided schema

🎯 REMEMBER: You are a precision SQL generator. Generate clean, efficient, and secure Cosmos DB SQL queries that directly answer the natural language question using the provided schema, ALWAYS including the TwinID partition filter and using correct Cosmos DB SELECT syntax.";
        }

        /// <summary>
        /// Creates a comprehensive prompt for SQL generation
        /// </summary>
        private string CreateSQLPrompt(string schema, string containerName, string question, string twinId)
        {
            return $@"
🎯 COSMOS DB SQL QUERY GENERATION TASK

📊 CONTAINER INFORMATION:
Container Name: {containerName}
Alias: c (use 'c' as the container alias in FROM clause)

📋 DATABASE SCHEMA:
{schema}

🔑 PARTITION KEY REQUIREMENT:
TwinID: {twinId} (MANDATORY - Must be included in WHERE clause as c.TwinID = '{twinId}')

❓ NATURAL LANGUAGE QUESTION:
{question}

🎯 TASK: Generate a Cosmos DB SQL SELECT statement that answers the question using the provided schema.

📝 SQL GENERATION RULES:
1. Use SELECT statement only
2. FROM {containerName} c (use 'c' as alias)
3. **MANDATORY**: Always include WHERE c.TwinID = '{twinId}' as the first condition
4. Access properties using c.fieldname or c[""fieldname""] syntax
5. Combine additional WHERE clauses using AND operator after the TwinID filter
6. Use appropriate string matching (CONTAINS, LIKE, =) as needed for additional conditions
7. For family relationships, consider fields like 'parentesco', 'apellido', 'nombre'
8. Return only the SQL query without any markdown formatting
9. **CRITICAL**: Use 'SELECT *' NOT 'SELECT c.*' - Cosmos DB doesn't support aliased wildcard

🔍 EXAMPLE PATTERNS (CORRECT COSMOS DB SYNTAX):
- For ""find family members with last name Luna"": 
  SELECT * FROM {containerName} c WHERE c.TwinID = '{twinId}' AND c.apellido = 'Luna'
- For ""find all parents"": 
  SELECT * FROM {containerName} c WHERE c.TwinID = '{twinId}' AND (c.parentesco = 'Padre' OR c.parentesco = 'Madre')
- For ""find people born in Mexico"": 
  SELECT * FROM {containerName} c WHERE c.TwinID = '{twinId}' AND c.pais_nacimiento = 'Mexico'
- For specific fields only:
  SELECT c.nombre, c.apellido, c.parentesco FROM {containerName} c WHERE c.TwinID = '{twinId}' AND c.genero = 'Masculino'

⚠️ CRITICAL: 
- Every query MUST start with WHERE c.TwinID = '{twinId}' for proper partitioning and performance
- Use 'SELECT *' NOT 'SELECT c.*' to avoid SC1001 syntax errors

Generate the SQL query now:";
        }

        /// <summary>
        /// Extracts clean SQL query from AI response
        /// </summary>
        private string ExtractSQLQuery(string aiResponse)
        {
            if (string.IsNullOrEmpty(aiResponse))
                return "";

            // Remove markdown code blocks if present
            string sql = aiResponse.Trim();
            
            // Remove ```sql or ``` markdown
            if (sql.StartsWith("```sql", StringComparison.OrdinalIgnoreCase))
            {
                sql = sql.Substring(6);
            }
            else if (sql.StartsWith("```"))
            {
                sql = sql.Substring(3);
            }
            
            if (sql.EndsWith("```"))
            {
                sql = sql.Substring(0, sql.Length - 3);
            }

            // Clean up the query
            sql = sql.Trim();
            
            // Ensure it's a SELECT statement
            if (!sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Generated query must be a SELECT statement");
            }

            // Fix common Cosmos DB syntax issues
            // Replace "SELECT c.*" with "SELECT *" to avoid SC1001 syntax error
            if (sql.Contains("SELECT c.*", StringComparison.OrdinalIgnoreCase))
            {
                sql = sql.Replace("SELECT c.*", "SELECT *", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine("🔧 Fixed Cosmos DB syntax: Replaced 'SELECT c.*' with 'SELECT *'");
            }

            return sql;
        }

        /// <summary>
        /// Validates input for SQL injection attempts
        /// </summary>
        private bool ContainsSQLInjection(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;

            string lowerInput = input.ToLower();
            string[] sqlInjectionPatterns = 
            {
                "drop ", "delete ", "insert ", "update ", "alter ", "create ", 
                "truncate ", "exec ", "execute ", "sp_", "xp_", "--", "/*", "*/",
                "union select", "' or '", "1=1", "1 = 1"
            };

            return sqlInjectionPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        /// <summary>
        /// Validates input for prompt injection attempts
        /// </summary>
        private bool ContainsPromptInjection(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;

            string lowerInput = input.ToLower();
            string[] injectionPatterns = 
            {
                "ignore previous", "forget everything", "system prompt", "override",
                "admin access", "root access", "bypass", "jailbreak", "disable safety"
            };

            return injectionPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        /// <summary>
        /// Executes the AI-generated SQL query against Cosmos DB and returns results as a formatted string
        /// </summary>
        /// <param name="sqlQuery">The SQL query to execute</param>
        /// <param name="containerName">Name of the Cosmos DB container</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <returns>Formatted string containing query results</returns>
        public async Task<string> ExecuteSQLQuery(string sqlQuery, string containerName, string twinId)
        {
            if (string.IsNullOrEmpty(sqlQuery))
            {
                throw new ArgumentException("SQL query cannot be null or empty", nameof(sqlQuery));
            }

            if (string.IsNullOrEmpty(containerName))
            {
                throw new ArgumentException("Container name cannot be null or empty", nameof(containerName));
            }

            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            // Check if Cosmos DB connection is configured
            if (string.IsNullOrEmpty(_cosmosConnectionString))
            {
                // Return mock/test data instead of throwing exception for testing scenarios
                return GenerateTestMockResponse(sqlQuery, containerName, twinId);
            }

            try
            {
                // Validate that it's a SELECT query for security
                if (!sqlQuery.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Only SELECT queries are allowed for execution");
                }

                using CosmosClient cosmosClient = new CosmosClient(_cosmosConnectionString);
                Database database = cosmosClient.GetDatabase(_cosmosDatabaseName);
                Container container = database.GetContainer(containerName);

                // Create query definition with partition key
                QueryDefinition queryDefinition = new QueryDefinition(sqlQuery);
                
                // Use partition key for efficient querying
                PartitionKey partitionKey = new PartitionKey(twinId);

                // Execute query
                using FeedIterator<dynamic> feed = container.GetItemQueryIterator<dynamic>(
                    queryDefinition,
                    requestOptions: new QueryRequestOptions
                    {
                        PartitionKey = partitionKey,
                        MaxItemCount = 100 // Limit results for safety
                    });

                StringBuilder resultBuilder = new StringBuilder();
                resultBuilder.AppendLine("🔍 Cosmos DB Query Results:");
                resultBuilder.AppendLine($"📝 Query: {sqlQuery}");
                resultBuilder.AppendLine($"🗂️ Container: {containerName}");
                resultBuilder.AppendLine($"🔑 Partition Key (TwinID): {twinId}");
                resultBuilder.AppendLine();

                int itemCount = 0;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<dynamic> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count == 0)
                    {
                        resultBuilder.AppendLine("❌ No results found for the query.");
                        break;
                    }

                    foreach (dynamic item in response)
                    {
                        itemCount++;
                        resultBuilder.AppendLine($"📄 Result {itemCount}:");
                        
                        // Convert dynamic item to clean formatted string using Newtonsoft.Json
                        string cleanJson = JsonConvert.SerializeObject(item, Formatting.Indented);
                        resultBuilder.AppendLine(cleanJson);
                        resultBuilder.AppendLine();
                    }
                }

                // Add summary information
                resultBuilder.AppendLine("📊 Query Summary:");
                resultBuilder.AppendLine($"   • Total Results: {itemCount}");
                resultBuilder.AppendLine($"   • Request Units (RU) Consumed: {totalRU:F2}");
                resultBuilder.AppendLine($"   • Database: {_cosmosDatabaseName}");
                resultBuilder.AppendLine($"   • Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

                Console.WriteLine($"✅ Successfully executed query. Found {itemCount} results. RU consumed: {totalRU:F2}");

                return resultBuilder.ToString();
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode}, RU: {cosmosEx.RequestCharge})";
                Console.WriteLine($"❌ {errorMessage}");
                return $"❌ Cosmos DB Error:\n{errorMessage}\n\nQuery: {sqlQuery}";
            }
            catch (Exception ex)
            {
                string errorMessage = $"Unexpected error executing query: {ex.Message}";
                Console.WriteLine($"❌ {errorMessage}");
                return $"❌ Execution Error:\n{errorMessage}\n\nQuery: {sqlQuery}";
            }
        }

        /// <summary>
        /// Generates a mock response for testing scenarios when Cosmos DB is not configured
        /// </summary>
        private string GenerateTestMockResponse(string sqlQuery, string containerName, string twinId)
        {
            StringBuilder mockBuilder = new StringBuilder();
            mockBuilder.AppendLine("🧪 TEST MODE - Mock Cosmos DB Query Results:");
            mockBuilder.AppendLine($"📝 Query: {sqlQuery}");
            mockBuilder.AppendLine($"🗂️ Container: {containerName}");
            mockBuilder.AppendLine($"🔑 Partition Key (TwinID): {twinId}");
            mockBuilder.AppendLine();
            
            // Generate mock family data based on the query type
            if (sqlQuery.ToLower().Contains("daniel"))
            {
                mockBuilder.AppendLine("📄 Result 1:");
                mockBuilder.AppendLine(@"{
  ""id"": ""mock-daniel-001"",
  ""twinId"": """ + twinId + @""",
  ""nombre"": ""Daniel"",
  ""apellido"": ""Luna"",
  ""parentesco"": ""Hermano"",
  ""email"": ""daniel.luna@example.com"",
  ""ocupacion"": ""Engineer"",
  ""genero"": ""Masculino"",
  ""pais_nacimiento"": ""Mexico""
}");
                mockBuilder.AppendLine();
            }
            else if (sqlQuery.ToLower().Contains("padre") || sqlQuery.ToLower().Contains("madre") || sqlQuery.ToLower().Contains("parent"))
            {
                mockBuilder.AppendLine("📄 Result 1:");
                mockBuilder.AppendLine(@"{
  ""id"": ""mock-parent-001"",
  ""twinId"": """ + twinId + @""",
  ""nombre"": ""Carlos"",
  ""apellido"": ""Luna"",
  ""parentesco"": ""Padre"",
  ""ocupacion"": ""Doctor""
}");
                mockBuilder.AppendLine();
                
                mockBuilder.AppendLine("📄 Result 2:");
                mockBuilder.AppendLine(@"{
  ""id"": ""mock-parent-002"",
  ""twinId"": """ + twinId + @""",
  ""nombre"": ""Maria"",
  ""apellido"": ""Luna"",
  ""parentesco"": ""Madre"",
  ""ocupacion"": ""Teacher""
}");
                mockBuilder.AppendLine();
            }
            else
            {
                mockBuilder.AppendLine("📄 Result 1:");
                mockBuilder.AppendLine(@"{
  ""id"": ""mock-family-001"",
  ""twinId"": """ + twinId + @""",
  ""nombre"": ""Sample"",
  ""apellido"": ""Family"",
  ""parentesco"": ""Hermano"",
  ""email"": ""sample@example.com""
}");
                mockBuilder.AppendLine();
            }

            // Add mock summary
            mockBuilder.AppendLine("📊 Mock Query Summary:");
            mockBuilder.AppendLine("   • Total Results: 1-2 (Mock Data)");
            mockBuilder.AppendLine("   • Request Units (RU) Consumed: 0.00 (Test Mode)");
            mockBuilder.AppendLine($"   • Database: {_cosmosDatabaseName} (Mock)");
            mockBuilder.AppendLine($"   • Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            mockBuilder.AppendLine();
            mockBuilder.AppendLine("⚠️ NOTE: This is mock data for testing purposes. Real Cosmos DB connection not configured.");

            Console.WriteLine($"🧪 Mock execution for testing. Query: {sqlQuery}");

            return mockBuilder.ToString();
        }

        /// <summary>
        /// Generates SQL query using AI and executes it against Cosmos DB, returning formatted results
        /// </summary>
        /// <param name="schema">Database schema with field mappings</param>
        /// <param name="containerName">Name of the Cosmos DB container</param>
        /// <param name="question">Natural language question</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <returns>Formatted string containing query and results</returns>
        public async Task<string> AgentBuildAndExecuteSQL(string schema, string containerName, string question, string twinId)
        {
            try
            {
                // First, generate the SQL query using AI
                string sqlQuery = await AgentBuildSQL(schema, containerName, question, twinId);

                // Then execute the query and get results
                string results = await ExecuteSQLQuery(sqlQuery, containerName, twinId);

                return results;
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error in AI-powered query generation and execution: {ex.Message}";
                Console.WriteLine($"❌ {errorMessage}");
                return $"❌ AgentBuildAndExecuteSQL Error:\n{errorMessage}\n\nQuestion: {question}";
            }
        }
    }
}
