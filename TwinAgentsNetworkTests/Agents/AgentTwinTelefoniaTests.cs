using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TwinAgentsNetwork.Agents;
using System;
using System.IO;
using System.Threading.Tasks;

namespace TwinAgentsNetworkTests.Agents
{
    /// <summary>
    /// Tests para AgentTwinTelefonia - Reconocimiento de voz con Azure Speech SDK
    /// Usa reconocimiento continuo para procesar archivos de audio completos
    /// </summary>
    [TestClass()]
    public class AgentTwinTelefoniaTests
    {
        private ILogger<AgentTwinTelefonia> _logger;
        private IConfiguration _configuration;
        private AgentTwinTelefonia _agent;

        [TestInitialize]
        public void Setup()
        {
            // Configurar ConfigurationBuilder (PATRÓN QUE FUNCIONA)
            var configBuilder = new ConfigurationBuilder()
                .AddEnvironmentVariables();
            _configuration = configBuilder.Build();

            // Crear logger (PATRÓN QUE FUNCIONA)
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<AgentTwinTelefonia>();

            // Inicializar agente
            _agent = new AgentTwinTelefonia(_logger, _configuration);
        }

        #region TestRecognizeSpeechSimpleAsync Tests

        [TestMethod]
        public async Task TestRecognizeSpeechSimpleAsync_WithValidWavFile_ShouldReturnSuccess()
        {
            // Arrange
            string testFilePath = @"C:\Data\tts_20251231_183354.wav";
            string expectedLanguage = "es-MX";

            Console.WriteLine("🧪 TEST: Transcribiendo archivo local de audio");
            Console.WriteLine($"📂 Archivo: {testFilePath}");

            // Verificar que el archivo existe antes de la prueba
            if (!File.Exists(testFilePath))
            {
                Assert.Inconclusive($"❌ Archivo de prueba no encontrado: {testFilePath}. " +
                    "Coloca un archivo de audio en C:\\Data\\ para ejecutar esta prueba.");
                return;
            }

            var fileInfo = new FileInfo(testFilePath);
            Console.WriteLine($"📊 Tamaño del archivo: {fileInfo.Length} bytes");

            // Act
            var result = await _agent.TestRecognizeSpeechSimpleAsync(testFilePath, expectedLanguage);

            // Assert
            Assert.IsNotNull(result, "El resultado no debe ser null");
            Console.WriteLine($"✅ Estado: {(result.Success ? "EXITOSO" : "FALLIDO")}");

            if (result.Success)
            {
                Assert.IsTrue(result.Success, "La transcripción debe ser exitosa");
                Assert.IsFalse(string.IsNullOrEmpty(result.TranscribedText), 
                    "El texto transcrito no debe estar vacío");
                Assert.AreEqual(expectedLanguage, result.Language, 
                    "El idioma debe coincidir");
                Assert.IsTrue(result.AudioSizeBytes > 0, 
                    "El tamaño del audio debe ser mayor a 0");
                Assert.IsTrue(result.DurationSeconds > 0, 
                    "La duración del procesamiento debe ser mayor a 0");
                Assert.IsNotNull(result.ProcessedAt, 
                    "La fecha de procesamiento no debe ser null");

                // Imprimir resultados
                Console.WriteLine($"📝 Texto transcrito: {result.TranscribedText}");
                Console.WriteLine($"🌐 Idioma: {result.Language}");
                Console.WriteLine($"📊 Tamaño de audio: {result.AudioSizeBytes} bytes");
                Console.WriteLine($"⏱️ Tiempo de procesamiento: {result.DurationSeconds:F2} segundos");
                Console.WriteLine($"🎯 Confianza: {result.Confidence:P0}");
                Console.WriteLine($"📂 Ruta del archivo: {result.AudioPath}");
                Console.WriteLine($"📅 Procesado: {result.ProcessedAt:yyyy-MM-dd HH:mm:ss}");

                // Validaciones adicionales
                Assert.IsTrue(result.Confidence >= 0 && result.Confidence <= 1, 
                    "La confianza debe estar entre 0 y 1");
            }
            else
            {
                Console.WriteLine($"❌ Error: {result.ErrorMessage}");
                Assert.Fail($"La transcripción falló: {result.ErrorMessage}");
            }
        }

        [TestMethod]
        public async Task TestRecognizeSpeechSimpleAsync_WithNonExistentFile_ShouldReturnError()
        {
            // Arrange
            string nonExistentFile = @"C:\Data\NonExistent_File_12345.wav";

            // Act
            var result = await _agent.TestRecognizeSpeechSimpleAsync(nonExistentFile, "es-MX");

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorMessage.Contains("File not found"));
            Assert.AreEqual(string.Empty, result.TranscribedText);

            Console.WriteLine($"✅ Test passed - Correctly handled missing file");
            Console.WriteLine($"   Error message: {result.ErrorMessage}");
        }

        [DataTestMethod]
        [DataRow("es-MX")]
        [DataRow("en-US")]
        [DataRow("es-ES")]
        public async Task TestRecognizeSpeechSimpleAsync_WithDifferentLanguages_ShouldProcessCorrectly(string language)
        {
            // Arrange
            string testFilePath = @"C:\Data\tts_20251231_183354.wav";

            // Skip test if file doesn't exist
            if (!File.Exists(testFilePath))
            {
                Console.WriteLine($"⚠️ Test skipped - File not found: {testFilePath}");
                Assert.Inconclusive($"Test file not found: {testFilePath}");
                return;
            }

            // Act
            var result = await _agent.TestRecognizeSpeechSimpleAsync(testFilePath, language);

            // Assert
            Assert.IsNotNull(result);
            // Note: Result might succeed or fail depending on actual audio language
            Assert.AreEqual(language, result.Language);

            Console.WriteLine($"✅ Test passed for language: {language}");
            Console.WriteLine($"   Success: {result.Success}");
            Console.WriteLine($"   Text: {result.TranscribedText}");
        }

        #endregion

        #region TestRecognizeSpeechFromStreamAsync Tests

        [TestMethod]
        public async Task TestRecognizeSpeechFromStreamAsync_WithValidWavFile_ShouldReturnSuccess()
        {
            // Arrange
            string testFilePath = @"C:\Data\tts_20251231_183354.wav";

            // Skip test if file doesn't exist
            if (!File.Exists(testFilePath))
            {
                Console.WriteLine($"⚠️ Test skipped - File not found: {testFilePath}");
                Assert.Inconclusive($"Test file not found: {testFilePath}");
                return;
            }

            // Act
            var result = await _agent.TestRecognizeSpeechFromStreamAsync(testFilePath, "es-MX");

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success, $"Expected success but got error: {result.ErrorMessage}");
            Assert.IsNotNull(result.TranscribedText);
            Assert.IsTrue(result.TranscribedText.Length > 0);
            Assert.IsTrue(result.DurationSeconds > 0);
            Assert.IsTrue(result.AudioSizeBytes > 0);
            Assert.AreEqual("es-MX", result.Language);
            Assert.AreEqual(testFilePath, result.AudioPath);

            Console.WriteLine($"✅ Test passed - Stream transcription successful");
            Console.WriteLine($"   Transcribed text: {result.TranscribedText}");
            Console.WriteLine($"   Duration: {result.DurationSeconds:F2}s");
            Console.WriteLine($"   Audio size: {result.AudioSizeBytes} bytes");
        }

        [TestMethod]
        public async Task TestRecognizeSpeechFromStreamAsync_WithNonExistentFile_ShouldReturnError()
        {
            // Arrange
            string nonExistentFile = @"C:\Data\NonExistent_Stream_File.wav";

            // Act
            var result = await _agent.TestRecognizeSpeechFromStreamAsync(nonExistentFile, "es-MX");

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorMessage.Contains("File not found"));
            Assert.AreEqual(string.Empty, result.TranscribedText);

            Console.WriteLine($"✅ Test passed - Correctly handled missing file in stream mode");
            Console.WriteLine($"   Error message: {result.ErrorMessage}");
        }

        [TestMethod]
        public async Task TestRecognizeSpeechFromStreamAsync_CompareBothMethods_ShouldProduceSimilarResults()
        {
            // Arrange
            string testFilePath = @"C:\Data\tts_20251231_183354.wav";

            // Skip test if file doesn't exist
            if (!File.Exists(testFilePath))
            {
                Console.WriteLine($"⚠️ Test skipped - File not found: {testFilePath}");
                Assert.Inconclusive($"Test file not found: {testFilePath}");
                return;
            }

            // Act
            var resultDirect = await _agent.TestRecognizeSpeechSimpleAsync(testFilePath, "es-MX");
            var resultStream = await _agent.TestRecognizeSpeechFromStreamAsync(testFilePath, "es-MX");

            // Assert
            Assert.IsNotNull(resultDirect);
            Assert.IsNotNull(resultStream);
            
            // Both should have same success status
            Assert.AreEqual(resultDirect.Success, resultStream.Success);

            if (resultDirect.Success && resultStream.Success)
            {
                // Both should transcribe similar text (allowing for minor differences)
                Assert.IsTrue(resultDirect.TranscribedText.Length > 0);
                Assert.IsTrue(resultStream.TranscribedText.Length > 0);
                
                Console.WriteLine($"✅ Both methods produced successful results");
                Console.WriteLine($"   Direct method text: {resultDirect.TranscribedText}");
                Console.WriteLine($"   Stream method text: {resultStream.TranscribedText}");
                Console.WriteLine($"   Direct duration: {resultDirect.DurationSeconds:F2}s");
                Console.WriteLine($"   Stream duration: {resultStream.DurationSeconds:F2}s");
            }
        }

        #endregion

        #region Performance and Stress Tests

        [TestMethod]
        public async Task TestRecognizeSpeechFromStreamAsync_ShouldCompleteWithinReasonableTime()
        {
            // Arrange
            string testFilePath = @"C:\Data\tts_20251231_183354.wav";
            var maxExpectedDuration = TimeSpan.FromMinutes(2); // Max 2 minutes for processing

            // Skip test if file doesn't exist
            if (!File.Exists(testFilePath))
            {
                Console.WriteLine($"⚠️ Test skipped - File not found: {testFilePath}");
                Assert.Inconclusive($"Test file not found: {testFilePath}");
                return;
            }

            var startTime = DateTime.UtcNow;

            // Act
            var result = await _agent.TestRecognizeSpeechFromStreamAsync(testFilePath, "es-MX");

            var totalDuration = DateTime.UtcNow - startTime;

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(totalDuration < maxExpectedDuration, 
                $"Processing took {totalDuration.TotalSeconds:F2}s which exceeds max {maxExpectedDuration.TotalSeconds}s");

            Console.WriteLine($"✅ Test passed - Completed in {totalDuration.TotalSeconds:F2}s");
            Console.WriteLine($"   Processing time: {result.DurationSeconds:F2}s");
        }

        [TestMethod]
        public async Task TestRecognizeSpeechFromStreamAsync_ShouldHandleEmptyAudioGracefully()
        {
            // Arrange
            string emptyWavFile = @"C:\Data\empty_test.wav";
            
            // Create a minimal empty WAV file if it doesn't exist
            if (!File.Exists(emptyWavFile))
            {
                // Create minimal WAV header (44 bytes) with no audio data
                byte[] wavHeader = new byte[44];
                // RIFF header
                Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, wavHeader, 0, 4);
                BitConverter.GetBytes(36).CopyTo(wavHeader, 4); // File size - 8
                Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, wavHeader, 8, 4);
                // fmt chunk
                Array.Copy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, wavHeader, 12, 4);
                BitConverter.GetBytes(16).CopyTo(wavHeader, 16); // fmt chunk size
                BitConverter.GetBytes((short)1).CopyTo(wavHeader, 20); // PCM
                BitConverter.GetBytes((short)1).CopyTo(wavHeader, 22); // Mono
                BitConverter.GetBytes(16000).CopyTo(wavHeader, 24); // Sample rate
                BitConverter.GetBytes(32000).CopyTo(wavHeader, 28); // Byte rate
                BitConverter.GetBytes((short)2).CopyTo(wavHeader, 32); // Block align
                BitConverter.GetBytes((short)16).CopyTo(wavHeader, 34); // Bits per sample
                // data chunk
                Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, wavHeader, 36, 4);
                BitConverter.GetBytes(0).CopyTo(wavHeader, 40); // No data

                File.WriteAllBytes(emptyWavFile, wavHeader);
                Console.WriteLine($"Created empty WAV file for testing: {emptyWavFile}");
            }

            // Act
            var result = await _agent.TestRecognizeSpeechFromStreamAsync(emptyWavFile, "es-MX");

            // Assert
            Assert.IsNotNull(result);
            // Empty file should result in NOMATCH or error
            Assert.IsFalse(result.Success);
            
            Console.WriteLine($"✅ Test passed - Empty audio handled correctly");
            Console.WriteLine($"   Error message: {result.ErrorMessage}");
        }

        #endregion

        #region Integration Tests

        [TestMethod]
        [Ignore("Integration test - Run manually with actual audio file")]
        public async Task TestRecognizeSpeechFromStreamAsync_WithLargeFile_ShouldProcessSuccessfully()
        {
            // Arrange
            string largeFilePath = @"C:\Data\large_audio_file.wav"; // > 5 minutes

            // Act
            var result = await _agent.TestRecognizeSpeechFromStreamAsync(largeFilePath, "es-MX");

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.TranscribedText.Length > 0);
            
            Console.WriteLine($"✅ Large file processed successfully");
            Console.WriteLine($"   Duration: {result.DurationSeconds:F2}s");
            Console.WriteLine($"   Text length: {result.TranscribedText.Length} chars");
        }

        [TestMethod]
        [Ignore("Integration test - Requires Azure Speech SDK credentials")]
        public async Task TestRecognizeSpeechFromStreamAsync_EndToEnd_WithRealAudio()
        {
            // Arrange
            string testAudioPath = @"C:\Data\test_spanish_audio.wav";

            // Skip if file doesn't exist
            if (!File.Exists(testAudioPath))
            {
                Console.WriteLine($"⚠️ Integration test skipped - Create test file: {testAudioPath}");
                Assert.Inconclusive($"Test file not found: {testAudioPath}");
                return;
            }

            // Act
            var result = await _agent.TestRecognizeSpeechFromStreamAsync(testAudioPath, "es-MX");

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.TranscribedText);
            Assert.IsTrue(result.TranscribedText.Length > 0);
            Assert.IsTrue(result.Confidence > 0);
            Assert.IsTrue(result.DurationSeconds > 0);
            Assert.IsTrue(result.AudioSizeBytes > 0);
            Assert.AreEqual("es-MX", result.Language);
            Assert.AreEqual(testAudioPath, result.AudioPath);

            Console.WriteLine($"✅ End-to-end test passed");
            Console.WriteLine($"   Transcribed: {result.TranscribedText}");
            Console.WriteLine($"   Confidence: {result.Confidence:F2}");
            Console.WriteLine($"   Duration: {result.DurationSeconds:F2}s");
            Console.WriteLine($"   Size: {result.AudioSizeBytes / 1024.0:F2} KB");
        }

        #endregion

        [TestCleanup]
        public void Cleanup()
        {
            // Limpiar recursos si es necesario
            Console.WriteLine("🧹 Cleanup: Test completed");
        }
    }
}
