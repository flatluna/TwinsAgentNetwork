using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Example usage of AgentTwinImageDesigner for generating and editing images
    /// </summary>
    public class AgentTwinImageDesignerExample
    {
        public static async Task RunExamples()
        {
            Console.WriteLine("=== AGENT TWIN IMAGE DESIGNER EXAMPLES ===\n");

            // Setup configuration and logger
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<AgentTwinImageDesigner>();

            var imageDesigner = new AgentTwinImageDesigner(logger, configuration);

            // Example 1: Generate a new image from text
            await Example1_GenerateImage(imageDesigner);

            // Example 2: Edit an existing image
            await Example2_EditImage(imageDesigner);

            // Example 3: Edit image from file with mask
            await Example3_EditImageWithMask(imageDesigner);

            // Example 4: Generate multiple variations
            await Example4_GenerateMultipleVariations(imageDesigner);

            Console.WriteLine("\n? All examples completed!");
        }

        /// <summary>
        /// Example 1: Generate a new image from a text prompt
        /// </summary>
        static async Task Example1_GenerateImage(AgentTwinImageDesigner imageDesigner)
        {
            Console.WriteLine("\n?? Example 1: Generate New Image");
            Console.WriteLine("=".PadRight(60, '='));

            var prompt = "A modern, beautifully furnished living room with natural lighting, " +
                        "contemporary furniture, and plants";

            Console.WriteLine($"Prompt: {prompt}");
            Console.WriteLine("Generating image...");

            var result = await imageDesigner.GenerateImageAsync(
                prompt: prompt,
                size: "1536x1024",
                quality: "standard",
                numberOfImages: 1
            );

            if (result.Success)
            {
                Console.WriteLine($"? Success! Generated {result.Images.Count} image(s)");
                Console.WriteLine($"   Processing time: {result.ProcessingTimeSeconds:F2} seconds");
                Console.WriteLine($"   Size: {result.Size}");
                Console.WriteLine($"   Quality: {result.Quality}");

                // Save to disk
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "generated_images");
                var savedPaths = await imageDesigner.SaveImagesToDiskAsync(
                    result.Images,
                    outputDir,
                    "generated_living_room"
                );

                Console.WriteLine($"   Saved to: {savedPaths[0]}");
            }
            else
            {
                Console.WriteLine($"? Failed: {result.ErrorMessage}");
            }
        }

        /// <summary>
        /// Example 2: Edit an existing image
        /// </summary>
        static async Task Example2_EditImage(AgentTwinImageDesigner imageDesigner)
        {
            Console.WriteLine("\n?? Example 2: Edit Existing Image");
            Console.WriteLine("=".PadRight(60, '='));

            var imagePath = "input_image.png";
            
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"?? Skipping: Image file not found: {imagePath}");
                return;
            }

            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var prompt = "Add modern furniture to this empty room, keep the complete image intact, " +
                        "do not crop anything";

            Console.WriteLine($"Prompt: {prompt}");
            Console.WriteLine("Editing image...");

            var result = await imageDesigner.EditImageAsync(
                imageBytes: imageBytes,
                prompt: prompt,
                size: "1536x1024",
                quality: "standard",
                numberOfImages: 1
            );

            if (result.Success)
            {
                Console.WriteLine($"? Success! Generated {result.EditedImages.Count} edited image(s)");
                Console.WriteLine($"   Processing time: {result.ProcessingTimeSeconds:F2} seconds");
                Console.WriteLine($"   Mask used: {result.MaskUsed}");

                // Save edited images
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "edited_images");
                var savedPaths = await imageDesigner.SaveImagesToDiskAsync(
                    result.EditedImages,
                    outputDir,
                    "edited_room"
                );

                Console.WriteLine($"   Saved to: {savedPaths[0]}");
            }
            else
            {
                Console.WriteLine($"? Failed: {result.ErrorMessage}");
            }
        }

        /// <summary>
        /// Example 3: Edit image with a mask for targeted editing
        /// </summary>
        static async Task Example3_EditImageWithMask(AgentTwinImageDesigner imageDesigner)
        {
            Console.WriteLine("\n?? Example 3: Edit Image with Mask");
            Console.WriteLine("=".PadRight(60, '='));

            var imagePath = "room_photo.png";
            var maskPath = "mask.png";

            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"?? Skipping: Image file not found: {imagePath}");
                return;
            }

            var prompt = "Replace the marked area with a beautiful sofa and coffee table";

            Console.WriteLine($"Prompt: {prompt}");
            Console.WriteLine($"Using mask: {maskPath}");
            Console.WriteLine("Editing image with mask...");

            var result = await imageDesigner.EditImageFromFileAsync(
                imagePath: imagePath,
                prompt: prompt,
                maskPath: maskPath,
                size: "1536x1024",
                quality: "standard",
                numberOfImages: 1
            );

            if (result.Success)
            {
                Console.WriteLine($"? Success! Generated {result.EditedImages.Count} edited image(s)");
                Console.WriteLine($"   Processing time: {result.ProcessingTimeSeconds:F2} seconds");
                Console.WriteLine($"   Mask used: {result.MaskUsed}");

                // Save results
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "masked_edits");
                var savedPaths = await imageDesigner.SaveImagesToDiskAsync(
                    result.EditedImages,
                    outputDir,
                    "masked_edit"
                );

                Console.WriteLine($"   Saved to: {savedPaths[0]}");
            }
            else
            {
                Console.WriteLine($"? Failed: {result.ErrorMessage}");
            }
        }

        /// <summary>
        /// Example 4: Generate multiple image variations
        /// </summary>
        static async Task Example4_GenerateMultipleVariations(AgentTwinImageDesigner imageDesigner)
        {
            Console.WriteLine("\n?? Example 4: Generate Multiple Variations");
            Console.WriteLine("=".PadRight(60, '='));

            var prompt = "A cozy bedroom with warm lighting, wooden furniture, and comfortable bedding";

            Console.WriteLine($"Prompt: {prompt}");
            Console.WriteLine("Generating 3 variations...");

            var result = await imageDesigner.GenerateImageAsync(
                prompt: prompt,
                size: "1024x1024",
                quality: "standard",
                numberOfImages: 3
            );

            if (result.Success)
            {
                Console.WriteLine($"? Success! Generated {result.Images.Count} variations");
                Console.WriteLine($"   Processing time: {result.ProcessingTimeSeconds:F2} seconds");

                // Save all variations
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "variations");
                var savedPaths = await imageDesigner.SaveImagesToDiskAsync(
                    result.Images,
                    outputDir,
                    "bedroom_variation"
                );

                Console.WriteLine($"   Saved {savedPaths.Count} images:");
                foreach (var path in savedPaths)
                {
                    Console.WriteLine($"     • {Path.GetFileName(path)}");
                }
            }
            else
            {
                Console.WriteLine($"? Failed: {result.ErrorMessage}");
            }
        }

        /// <summary>
        /// Main program to run examples
        /// </summary>
        public static async Task Main(string[] args)
        {
            try
            {
                await RunExamples();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n? Error running examples: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }

    /// <summary>
    /// Utility class for testing specific scenarios
    /// </summary>
    public class ImageDesignerTestScenarios
    {
        /// <summary>
        /// Test scenario: Interior design enhancement
        /// </summary>
        public static async Task TestInteriorDesign()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<AgentTwinImageDesigner>();
            var designer = new AgentTwinImageDesigner(logger, configuration);

            Console.WriteLine("?? Testing Interior Design Enhancement");

            var prompts = new[]
            {
                "Add modern furniture to this living room, complete image, no cropping",
                "Transform this space into a contemporary office with desk and shelves",
                "Create a cozy reading nook with armchair and bookshelf"
            };

            foreach (var prompt in prompts)
            {
                Console.WriteLine($"\n?? Prompt: {prompt}");
                var result = await designer.GenerateImageAsync(prompt, "1536x1024", "standard");
                
                if (result.Success)
                {
                    Console.WriteLine($"   ? Generated in {result.ProcessingTimeSeconds:F2}s");
                }
                else
                {
                    Console.WriteLine($"   ? Failed: {result.ErrorMessage}");
                }
            }
        }
    }
}
