using System;
using System.Threading.Tasks;
using TwinAgentsNetwork.Utilities;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Example showing how to use reflection to extract FamilyData schema
    /// </summary>
    public class SchemaExtractionExample
    {
        public static void RunExample()
        {
            try
            {
                Console.WriteLine("=== FAMILY DATA SCHEMA EXTRACTION USING REFLECTION ===\n");

                // Extract schema using reflection
                string schema = SchemaExtractor.ExtractFamilyDataSchema();
                
                Console.WriteLine(schema);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error extracting schema: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Console application to test the schema extraction
    /// </summary>
    public class SchemaExtractionTestProgram
    {
        public static void Main(string[] args)
        {
            SchemaExtractionExample.RunExample();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}