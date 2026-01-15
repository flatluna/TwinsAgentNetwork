using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using OpenAI.Embeddings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinFx.Agents;
using static TwinAgentsNetwork.Services.NoStructuredServices;
using EmbeddingGenerationOptions = OpenAI.Embeddings.EmbeddingGenerationOptions;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Azure AI Search Service for No-Structured Documents indexing and search
    /// ========================================================================
    /// 
    /// This service creates and manages a search index optimized for no-structured documents with:
    /// - Vector search capabilities using Azure OpenAI embeddings for CapituloExtraido
    /// - Semantic search for natural language queries about document chapters
    /// - Full-text search across chapter content, summaries, and Q&A
    /// - Chapter-based search and filtering
    /// - Document processing tracking and filtering
    /// 
    /// Author: TwinFx Project
    /// Date: January 2025
    /// </summary>
    public class DocumentsNoStructuredIndex
    {
        private readonly ILogger<DocumentsNoStructuredIndex> _logger;
        private readonly IConfiguration _configuration;
        private readonly SearchIndexClient? _indexClient;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;

        // Configuration constants
        private const string IndexName = "no-structured-index";
        private const string VectorSearchProfile = "no-structured-vector-profile";
        private const string HnswAlgorithmConfig = "no-structured-hnsw-config";
        private const string SemanticConfig = "no-structured-semantic-config";
        private readonly int EmbeddingDimensions; // Changed from const to readonly to make it dynamic

        // Configuration keys
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private readonly string? _openAIEndpoint;
        private readonly string? _openAIApiKey;
        private readonly string? _embeddingDeployment;

        public DocumentsNoStructuredIndex(ILogger<DocumentsNoStructuredIndex> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Load Azure Search configuration
            _searchEndpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
            _searchApiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

            // 🔍 DIAGNOSTIC LOGGING - Add these lines to verify configuration
            _logger.LogInformation("🔍 DIAGNOSTICS: Azure Search Configuration");
            _logger.LogInformation("   📍 Search Endpoint: {Endpoint}", string.IsNullOrEmpty(_searchEndpoint) ? "NOT SET" : _searchEndpoint);
            _logger.LogInformation("   🔑 Search API Key: {KeyStatus}", string.IsNullOrEmpty(_searchApiKey) ? "NOT SET" : $"SET ({_searchApiKey.Length} chars)");
            
            // Load Azure OpenAI configuration
            _openAIEndpoint = GetConfigurationValue("AZURE_OPENAI_ENDPOINT") ?? GetConfigurationValue("AzureOpenAI:Endpoint");
            _openAIApiKey = GetConfigurationValue("AZURE_OPENAI_API_KEY") ?? GetConfigurationValue("AzureOpenAI:ApiKey");
            _embeddingDeployment = GetConfigurationValue("AZURE_OPENAI_EMBEDDING_DEPLOYMENT", "text-embedding-ada-002");
            _embeddingDeployment = "text-embedding-3-large";
            // Set embedding dimensions based on model type
            EmbeddingDimensions = GetEmbeddingDimensionsForModel(_embeddingDeployment);
            _logger.LogInformation("📐 Using {Dimensions} dimensions for embedding model: {Model}", EmbeddingDimensions, _embeddingDeployment);

            // Initialize Azure Search client
            if (!string.IsNullOrEmpty(_searchEndpoint) && !string.IsNullOrEmpty(_searchApiKey))
            {
                try
                {
                    var credential = new AzureKeyCredential(_searchApiKey);
                    _indexClient = new SearchIndexClient(new Uri(_searchEndpoint), credential);
                    _logger.LogInformation("📄 No-Structured Documents Search Index client initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure Search client for No-Structured Documents Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure Search credentials not found for No-Structured Documents Index");
            }

            // Initialize Azure OpenAI client for embeddings
            if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                    _logger.LogInformation("🤖 Azure OpenAI embedding client initialized for No-Structured Documents Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for No-Structured Documents Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure OpenAI credentials not found for No-Structured Documents Index");
            }
        }

        /// <summary>
        /// Get configuration value with fallback to Values section (Azure Functions format)
        /// </summary>
        private string? GetConfigurationValue(string key, string? defaultValue = null)
        {
            // Try direct key first
            var value = _configuration.GetValue<string>(key);

            // Try Values section if not found (Azure Functions format)
            if (string.IsNullOrEmpty(value))
            {
                value = _configuration.GetValue<string>($"Values:{key}");
            }

            return !string.IsNullOrEmpty(value) ? value : defaultValue;
        }

        /// <summary>
        /// Get embedding dimensions based on the model type
        /// </summary>
        private static int GetEmbeddingDimensionsForModel(string? embeddingModel)
        {
            // FIXED: Always use 1536 dimensions to maintain compatibility with existing index
            // The text-embedding-3-large model supports custom dimensions from 256 to 3072
            // Using 1536 provides good quality while maintaining index compatibility
            return 1536;

            /* ORIGINAL CODE - COMMENTED OUT TO FORCE 1536
            if (string.IsNullOrEmpty(embeddingModel))
                return 1536; // Default for ada-002

            return embeddingModel.ToLowerInvariant() switch
            {
                var model when model.Contains("text-embedding-ada-002") => 1536,
                var model when model.Contains("text-embedding-3-small") => 1536, // Can be customized up to 1536
                var model when model.Contains("text-embedding-3-large") => 3072, // Can be customized up to 3072
                var model when model.Contains("text-embedding-ada-003") => 1536, // Hypothetical future model
                _ => 1536 // Default fallback
            };
            */
        }

        /// <summary>
        /// Check if the no-structured documents search service is available
        /// </summary>
        public bool IsAvailable => _indexClient != null;

        /// <summary>
        /// Upload PDfDocumentNoStructured to no-structured-index
        /// Iterates through ChapterList and indexes subchapters based on token count
        /// </summary>
       
        /// <summary>
        /// Index an ExractedChapterSubsIndex in Azure AI Search
        /// </summary>
        public async Task<NoStructuredIndexResult> IndexExractedChapterSubsIndexAsync(ExractedChapterSubsIndex chapterSubsIndex)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                // Create search client for the no-structured documents index
                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Generate unique document ID
                var documentId = !string.IsNullOrEmpty(chapterSubsIndex.id)
                    ? chapterSubsIndex.id
                    : $"chap_{chapterSubsIndex.TwinID}_{chapterSubsIndex.ChapterID}_{DateTime.UtcNow:yyyyMMddHHmmss}";

                // Build comprehensive content for vector search
                var contenidoCompleto = BuildCompleteContent(chapterSubsIndex);

                // Generate embeddings for the complete content
                float[]? embeddings = null;
                if (_embeddingClient != null)
                {
                    embeddings = await GenerateEmbeddingsAsync(contenidoCompleto);
                }

                // Create search document based on ExractedChapterSubsIndex structure
                var searchDocument = new Dictionary<string, object>
                {
                    ["id"] = documentId,
                    ["ChapterTitle"] = chapterSubsIndex.ChapterTitle ?? "",
                    ["TwinID"] = chapterSubsIndex.TwinID ?? "",
                    ["ChapterID"] = chapterSubsIndex.ChapterID ?? "",
                    ["TotalTokensDocument"] = chapterSubsIndex.TotalTokensDocument,
                    ["FileName"] = chapterSubsIndex.FileName ?? "",
                    ["FilePath"] = chapterSubsIndex.FilePath ?? "",
                    ["TextChapter"] = chapterSubsIndex.TextChapter ?? "",
                    ["FromPageChapter"] = chapterSubsIndex.FromPageChapter,
                    ["ToPageChapter"] = chapterSubsIndex.ToPageChapter,
                    ["TotalTokens"] = chapterSubsIndex.TotalTokens,
                    ["TitleSub"] = chapterSubsIndex.TitleSub ?? "",
                    ["TextSub"] = chapterSubsIndex.TextSub ?? "",
                    ["TotalTokensSub"] = chapterSubsIndex.TotalTokensSub,
                    ["FromPageSub"] = chapterSubsIndex.FromPageSub,
                    ["ToPageSub"] = chapterSubsIndex.ToPageSub,
                    ["DateCreated"] = DateTimeOffset.UtcNow,
                    ["Subcategoria"] = chapterSubsIndex.Subcategoria,
                    ["ContenidoCompleto"] = contenidoCompleto
                };

                // Add vector embeddings if available
                if (embeddings != null)
                {
                    searchDocument["ContenidoVector"] = embeddings;
                }

                // Upload document to search index
                var documents = new[] { new SearchDocument(searchDocument) };
                var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(documents);

                var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (errors.Any())
                {
                    var errorMessages = errors.Select(e => e.ErrorMessage).ToList();
                    _logger.LogError("❌ Error indexing chapter: {ChapterTitle} - Errors: {Errors}",
                        chapterSubsIndex.ChapterTitle, string.Join(", ", errorMessages));

                    return new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = $"Error indexing chapter: {string.Join(", ", errorMessages)}"
                    };
                }

                _logger.LogInformation("✅ ExractedChapterSubsIndex indexed successfully: {ChapterTitle}", chapterSubsIndex.ChapterTitle);

                return new NoStructuredIndexResult
                {
                    Success = true,
                    Message = $"Chapter '{chapterSubsIndex.ChapterTitle}' indexed successfully",
                    IndexName = IndexName,
                    DocumentId = documentId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error indexing ExractedChapterSubsIndex: {ChapterID}", chapterSubsIndex.ChapterID);
                return new NoStructuredIndexResult
                {
                    Success = false,
                    Error = $"Error indexing chapter: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Search chapters by Estructura and TwinID with semantic search capabilities
        /// </summary>
        public async Task<NoStructuredSearchResult> SearchByEstructuraAndTwinAsync(string estructura, string twinId, string? searchQuery = null, int top = 1000)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredSearchResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📄 Searching documents by Estructura: {Estructura}, TwinID: {TwinId}, Query: {SearchQuery}",
                    estructura, twinId, searchQuery ?? "*");

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Size = top,
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Simple
                };

                // Build filter for TwinID and optionally by FileName (estructura parameter used as fileName filter)
                var filterParts = new List<string>();

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                // Use estructura as FileName filter if provided and not empty
                if (!string.IsNullOrEmpty(estructura) && estructura != "*")
                {
                    filterParts.Add($"FileName eq '{estructura.Replace("'", "''")}'");
                }

                if (filterParts.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filterParts);
                }

                // Select all relevant fields including content for full search results
                var fieldsToSelect = new[]
                {
                    "id", "ChapterTitle", "TwinID", "ChapterID", "TotalTokensDocument",
                    "FileName", "FilePath", "TextChapter", "FromPageChapter", "ToPageChapter",
                    "TotalTokens", "TitleSub", "TextSub", "TotalTokensSub", "FromPageSub",
                    "ToPageSub", "DateCreated"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Enable semantic search if query is provided
                if (!string.IsNullOrEmpty(searchQuery))
                {
                    searchOptions.QueryType = SearchQueryType.Semantic;
                    searchOptions.SemanticSearch = new()
                    {
                        SemanticConfigurationName = SemanticConfig,
                        QueryCaption = new(QueryCaptionType.Extractive),
                        QueryAnswer = new(QueryAnswerType.Extractive)
                    };
                }

                // Use search query if provided, otherwise use "*" to get all matching documents
                var searchText = !string.IsNullOrEmpty(searchQuery) ? searchQuery : "*";

                var searchResponse = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);
                var searchResults = new List<ExractedChapterSubsIndex>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new ExractedChapterSubsIndex
                    {
                        id = result.Document.GetString("id") ?? string.Empty,
                        ChapterTitle = result.Document.GetString("ChapterTitle") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        ChapterID = result.Document.GetString("ChapterID") ?? string.Empty,
                        TotalTokensDocument = result.Document.GetInt32("TotalTokensDocument") ?? 0,
                        FileName = result.Document.GetString("FileName") ?? string.Empty,
                        FilePath = result.Document.GetString("FilePath") ?? string.Empty,

                        // Chapter fields with full content
                        TextChapter = result.Document.GetString("TextChapter") ?? string.Empty,
                        FromPageChapter = result.Document.GetInt32("FromPageChapter") ?? 0,
                        ToPageChapter = result.Document.GetInt32("ToPageChapter") ?? 0,
                        TotalTokens = result.Document.GetInt32("TotalTokens") ?? 0,

                        // Subchapter fields with full content
                        TitleSub = result.Document.GetString("TitleSub") ?? string.Empty,
                        TextSub = result.Document.GetString("TextSub") ?? string.Empty,
                        TotalTokensSub = result.Document.GetInt32("TotalTokensSub") ?? 0,
                        FromPageSub = result.Document.GetInt32("FromPageSub") ?? 0,
                        ToPageSub = result.Document.GetInt32("ToPageSub") ?? 0
                    };

                    searchResults.Add(chapterItem);
                }

                // Group by FileName to create document summaries with chapters
                var groupedByFileName = searchResults
                    .GroupBy(chapter => chapter.FileName)
                    .Select(group => new NoStructuredDocument
                    {
                        DocumentID = group.Key, // Using FileName as DocumentID for grouping
                        TwinID = group.First().TwinID,
                        Estructura = estructura ?? "no-estructurado", // Use the parameter or default
                        Subcategoria = "general", // Default since we don't have this field in the index
                        TotalChapters = group.Count(),
                        TotalTokens = group.Sum(c => c.TotalTokens + c.TotalTokensSub),
                        TotalPages = group.Any() ? CalculateTotalPages(group) : 0,
                        ProcessedAt = DateTimeOffset.UtcNow, // We don't have this in index, use current time
                        SearchScore = 1.0, // Default score, could be enhanced with actual search scores

                        // Convert chapters to search result items
                        Capitulos = group.Select(chapter => new NoStructuredSearchResultItem
                        {
                            Id = chapter.id,
                            DocumentID = chapter.FileName,
                            CapituloID = chapter.ChapterID,
                            TwinID = chapter.TwinID,
                            SearchScore = 1.0, // Could be enhanced with actual search scores
                            Titulo = !string.IsNullOrEmpty(chapter.TitleSub)
                                ? chapter.TitleSub
                                : chapter.ChapterTitle,
                            NumeroCapitulo = ExtractChapterNumber(chapter.ChapterTitle),
                            PaginaDe = chapter.FromPageSub > 0 ? chapter.FromPageSub : chapter.FromPageChapter,
                            PaginaA = chapter.ToPageSub > 0 ? chapter.ToPageSub : chapter.ToPageChapter,
                            Nivel = DetermineChapterLevel(chapter), // Determine level based on whether it's a subchapter
                            TotalTokens = chapter.TotalTokensSub > 0 ? chapter.TotalTokensSub : chapter.TotalTokens
                        }).ToList()
                    })
                    .OrderBy(doc => doc.DocumentID)
                    .ToList();

                _logger.LogInformation("✅ Found {ChapterCount} chapters grouped into {DocumentCount} documents for TwinID: {TwinId}, Estructura: {Estructura}",
                    searchResults.Count, groupedByFileName.Count, twinId, estructura);

                return new NoStructuredSearchResult
                {
                    Success = true,
                    Documents = groupedByFileName,
                    TotalChapters = searchResponse.Value.TotalCount ?? 0,
                    TotalDocuments = groupedByFileName.Count,
                    SearchQuery = searchQuery ?? "*",
                    SearchType = !string.IsNullOrEmpty(searchQuery) ? "SemanticSearch" : "FilterByTwinIdAndEstructura",
                    Message = $"Found {groupedByFileName.Count} documents with {searchResults.Count} total chapters for TwinID '{twinId}' and Estructura '{estructura}'"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching documents by Estructura: {Estructura}, TwinID: {TwinId}", estructura, twinId);
                return new NoStructuredSearchResult
                {
                    Success = false,
                    Error = $"Error searching documents: {ex.Message}",
                    SearchType = "FilterByTwinIdAndEstructura"
                };
            }
        }

        /// <summary>
        /// Calculate total pages from a group of chapters
        /// </summary>
        private static int CalculateTotalPages(IGrouping<string, ExractedChapterSubsIndex> group)
        {
            var minPage = group
                .Select(c => Math.Min(
                    c.FromPageChapter == 0 ? c.FromPageSub : c.FromPageChapter,
                    c.FromPageSub == 0 ? c.FromPageChapter : c.FromPageSub))
                .Where(p => p > 0)
                .DefaultIfEmpty(1)
                .Min();

            var maxPage = group
                .Select(c => Math.Max(c.ToPageChapter, c.ToPageSub))
                .Where(p => p > 0)
                .DefaultIfEmpty(1)
                .Max();

            return Math.Max(1, maxPage - minPage + 1);
        }

        /// <summary>
        /// Extract chapter number from chapter title
        /// </summary>
        private static string ExtractChapterNumber(string chapterTitle)
        {
            if (string.IsNullOrEmpty(chapterTitle))
                return "1";

            // Try to extract number patterns like "1.", "Cap 1", "Chapter 1", "1.1", etc.
            var match = System.Text.RegularExpressions.Regex.Match(chapterTitle, @"(\d+(?:\.\d+)*)");
            return match.Success ? match.Groups[1].Value : "1";
        }

        /// <summary>
        /// Determine chapter level based on whether it's a subchapter or main chapter
        /// </summary>
        private static int DetermineChapterLevel(ExractedChapterSubsIndex chapter)
        {
            // If it has subchapter content, it's level 2, otherwise level 1
            if (!string.IsNullOrEmpty(chapter.TitleSub) && chapter.TotalTokensSub > 0)
                return 2;

            return 1;
        }

        /// <summary>
        /// Search documents metadata by TwinID and FileName - Returns only document metadata without chapter content
        /// </summary>
        public async Task<NoStructuredSearchMetadataResult> SearchDocumentMetadataByEstructuraAndTwinAsync(string estructura,
            string twinId, string? searchQuery = null, int top = 1000)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredSearchMetadataResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📄 Searching documents metadata by TwinID: {TwinId}, FileName: {FileName}", twinId, estructura);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Size = top,
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Simple
                };

                // Build filter for TwinID and optionally by FileName (estructura parameter used as fileName filter)
                var filterParts = new List<string>();

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }


                if (filterParts.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filterParts);
                }

                // Select only essential fields for metadata - NO heavy content fields
                var fieldsToSelect = new[]
                {
                    "id", "ChapterTitle", "TwinID", "ChapterID", "TotalTokensDocument",
                    "FileName", "FilePath", "FromPageChapter", "ToPageChapter",
                    "TotalTokens", "TitleSub", "TotalTokensSub", "FromPageSub", "Subcategoria",
                    "ToPageSub", "DateCreated"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Use search query if provided, otherwise use "*" to get all matching documents
                var searchText = !string.IsNullOrEmpty(searchQuery) ? searchQuery : "*";

                var searchResponse = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);
                var searchResults = new List<ExractedChapterSubsIndex>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new ExractedChapterSubsIndex
                    {
                        id = result.Document.GetString("id") ?? string.Empty,
                        ChapterTitle = result.Document.GetString("ChapterTitle") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        ChapterID = result.Document.GetString("ChapterID") ?? string.Empty,
                        TotalTokensDocument = result.Document.GetInt32("TotalTokensDocument") ?? 0,
                        FileName = result.Document.GetString("FileName") ?? string.Empty,
                        FilePath = result.Document.GetString("FilePath") ?? string.Empty,
                        Subcategoria = result.Document.GetString("Subcategoria") ?? string.Empty,

                        // Chapter fields - NO TextChapter to keep response lightweight
                        TextChapter = "", // Excluded for metadata response
                        FromPageChapter = result.Document.GetInt32("FromPageChapter") ?? 0,
                        ToPageChapter = result.Document.GetInt32("ToPageChapter") ?? 0,
                        TotalTokens = result.Document.GetInt32("TotalTokens") ?? 0,

                        // Subchapter fields - NO TextSub to keep response lightweight  
                        TitleSub = result.Document.GetString("TitleSub") ?? string.Empty,
                        TextSub = "", // Excluded for metadata response
                        TotalTokensSub = result.Document.GetInt32("TotalTokensSub") ?? 0,
                        FromPageSub = result.Document.GetInt32("FromPageSub") ?? 0,
                        ToPageSub = result.Document.GetInt32("ToPageSub") ?? 0
                    };

                    searchResults.Add(chapterItem);
                }

                // Group by FileName to create document metadata summaries
                var groupedByFileName = searchResults
                    .GroupBy(chapter => chapter.FileName)
                    .Select(group => new NoStructuredDocumentMetadata
                    {
                        DocumentID = group.Key, // Using FileName as DocumentID for grouping
                        TwinID = group.First().TwinID,
                        Estructura = estructura ?? "no-estructurado", // Use the parameter or default
                        Subcategoria = group.First().Subcategoria,

                        // FIX: Count unique chapters using ChapterID to avoid counting duplicates
                        TotalChapters = group
                            .Where(c => !string.IsNullOrEmpty(c.ChapterID)) // Only count chapters with ChapterID
                            .GroupBy(c => c.ChapterID) // Group by ChapterID to get unique chapters
                            .Count(), // Count unique ChapterIDs

                        TotalTokens = group.Sum(c => c.TotalTokens + c.TotalTokensSub),
                        TotalPages = group.Any() ? group.Max(c => Math.Max(c.ToPageChapter, c.ToPageSub)) - group.Min(c => Math.Min(c.FromPageChapter == 0 ? c.FromPageSub : c.FromPageChapter, c.FromPageSub == 0 ? c.FromPageChapter : c.FromPageSub)) + 1 : 0,
                        ProcessedAt = DateTimeOffset.UtcNow, // We don't have this in index, use current time
                        SearchScore = 1.0 // Default score since this is metadata search
                    })
                    .OrderBy(doc => doc.DocumentID)
                    .ToList();

                _logger.LogInformation("✅ Found {ChapterCount} chapters grouped into {DocumentCount} documents metadata for TwinID: {TwinId}",
                    searchResults.Count, groupedByFileName.Count, twinId);

                return new NoStructuredSearchMetadataResult
                {
                    Success = true,
                    Documents = groupedByFileName,
                    TotalChapters = searchResponse.Value.TotalCount ?? 0,
                    TotalDocuments = groupedByFileName.Count,
                    SearchQuery = searchQuery ?? "*",
                    SearchType = "FilterByTwinIdAndFileName_MetadataOnly",
                    Message = $"Found {groupedByFileName.Count} documents metadata with {searchResults.Count} total chapters for TwinID '{twinId}'"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching documents metadata by TwinID: {TwinId}, FileName: {FileName}", twinId, estructura);
                return new NoStructuredSearchMetadataResult
                {
                    Success = false,
                    Error = $"Error searching documents metadata: {ex.Message}",
                    SearchType = "FilterByTwinIdAndFileName_MetadataOnly"
                };
            }
        }

        /// <summary>
        /// Get a specific document with all its chapters by TwinID and FileName
        /// Returns a list of ExractedChapterSubsIndex matching the specified FileName and TwinID
        /// </summary>
        /// <param name="twinId">Twin ID to filter by</param>
        /// <param name="fileName">FileName to search for (this is the documentId parameter but represents FileName)</param>
        /// <returns>List of ExractedChapterSubsIndex matching the criteria</returns>
        public async Task<List<ExractedChapterSubsIndex>> GetDocumentByTwinIdAndDocumentIdAsync(string twinId, string fileName)
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.LogWarning("⚠️ Azure Search service not available");
                    return new List<ExractedChapterSubsIndex>();
                }

                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogWarning("⚠️ TwinID cannot be null or empty");
                    return new List<ExractedChapterSubsIndex>();
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    _logger.LogWarning("⚠️ FileName cannot be null or empty");
                    return new List<ExractedChapterSubsIndex>();
                }

                _logger.LogInformation("📄 Getting document chapters by TwinID: {TwinId}, FileName: {FileName}", twinId, fileName);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Size = 1000, // Get up to 1000 chapters for a single document
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Simple
                };

                // Build filter for both TwinID and FileName - both are required
                var filterParts = new List<string>
                {
                    $"TwinID eq '{twinId.Replace("'", "''")}'",
                    $"FileName eq '{fileName.Replace("'", "''")}'"
                };

                searchOptions.Filter = string.Join(" and ", filterParts);

                // Select all fields for complete ExractedChapterSubsIndex objects including content
                var fieldsToSelect = new[]
                {
                    "id", "ChapterTitle", "TwinID", "ChapterID", "TotalTokensDocument",
                    "FileName", "FilePath", "TextChapter", "FromPageChapter", "ToPageChapter",
                    "TotalTokens", "TitleSub", "TextSub", "TotalTokensSub", "FromPageSub",
                    "ToPageSub", "DateCreated", "Subcategoria"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                _logger.LogInformation("🔍 Searching with filter: {Filter}", searchOptions.Filter);

                var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
                var searchResults = new List<ExractedChapterSubsIndex>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new ExractedChapterSubsIndex
                    {
                        id = result.Document.GetString("id") ?? string.Empty,
                        ChapterTitle = result.Document.GetString("ChapterTitle") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        ChapterID = result.Document.GetString("ChapterID") ?? string.Empty,
                        TotalTokensDocument = result.Document.GetInt32("TotalTokensDocument") ?? 0,
                        FileName = result.Document.GetString("FileName") ?? string.Empty,
                        FilePath = result.Document.GetString("FilePath") ?? string.Empty,

                        // Chapter fields with full content
                        TextChapter = result.Document.GetString("TextChapter") ?? string.Empty,
                        FromPageChapter = result.Document.GetInt32("FromPageChapter") ?? 0,
                        ToPageChapter = result.Document.GetInt32("ToPageChapter") ?? 0,
                        TotalTokens = result.Document.GetInt32("TotalTokens") ?? 0,

                        // Subchapter fields with full content
                        TitleSub = result.Document.GetString("TitleSub") ?? string.Empty,
                        TextSub = result.Document.GetString("TextSub") ?? string.Empty,
                        TotalTokensSub = result.Document.GetInt32("TotalTokensSub") ?? 0,
                        FromPageSub = result.Document.GetInt32("FromPageSub") ?? 0,
                        ToPageSub = result.Document.GetInt32("ToPageSub") ?? 0
                    };

                    searchResults.Add(chapterItem);
                }

                // Sort results by page order for better organization
                var sortedResults = searchResults
                    .OrderBy(c => Math.Min(c.FromPageChapter == 0 ? c.FromPageSub : c.FromPageChapter,
                                          c.FromPageSub == 0 ? c.FromPageChapter : c.FromPageSub))
                    .ThenBy(c => c.ChapterTitle)
                    .ThenBy(c => c.TitleSub)
                    .ToList();

                // Generate SAS URL once for all chapters since they belong to the same file
                string? sasUrl = null;
                if (sortedResults.Count > 0)
                {
                    try
                    {
                        _logger.LogInformation("🔗 Generating SAS URL for document file: {FileName}", fileName);

                        // Create DataLake client using the existing configuration pattern
                        var serviceProvider = new ServiceCollection()
                            .AddLogging(builder => builder.AddConsole())
                            .BuildServiceProvider();
                        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

                        var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                        // Get the file path from the first result (all chapters have the same file)
                        var firstResult = sortedResults.First();
                        string filePath = !string.IsNullOrEmpty(firstResult.FilePath) ? firstResult.FilePath : fileName;

                        // Clean the file path by removing twin ID prefix if present
                        filePath = CleanFilePath(filePath, twinId);
                        filePath = filePath + "/" + fileName;
                        // Generate SAS URL valid for 24 hours
                        sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

                        if (!string.IsNullOrEmpty(sasUrl))
                        {
                            _logger.LogInformation("✅ Generated SAS URL for file: {FilePath}", filePath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Could not generate SAS URL for file: {FilePath}", filePath);
                        }
                    }
                    catch (Exception sasEx)
                    {
                        _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for file: {FileName}, continuing without it", fileName);
                        // Continue without SAS URL - not critical for the operation
                    }

                    // Set the SAS URL for all chapters (they all belong to the same file)
                    foreach (var chapter in sortedResults)
                    {
                        chapter.fileURL = sasUrl ?? string.Empty;
                    }

                    if (!string.IsNullOrEmpty(sasUrl))
                    {
                        _logger.LogInformation("📎 Set SAS URL for {ChapterCount} chapters", sortedResults.Count);
                    }
                }

                _logger.LogInformation("✅ Found {ChapterCount} chapters for TwinID: {TwinId}, FileName: {FileName}",
                    sortedResults.Count, twinId, fileName);

                return sortedResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting document chapters by TwinID: {TwinId}, FileName: {FileName}", twinId, fileName);
                return new List<ExractedChapterSubsIndex>();
            }
        }

        /// <summary>
        /// Helper method to remove twin ID from file path if present
        /// </summary>
        /// <param name="filePath">Original file path</param>
        /// <param name="twinId">Twin ID to remove from path</param>
        /// <returns>Clean path without twin ID prefix</returns>
        private static string CleanFilePath(string filePath, string twinId)
        {
            if (string.IsNullOrEmpty(filePath))
                return filePath;

            // Remove twin ID prefix if present (handles both with and without trailing slash)
            var twinIdPrefix = $"{twinId}/";
            if (filePath.StartsWith(twinIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(twinIdPrefix.Length);
            }

            // Also check without slash
            if (filePath.StartsWith(twinId, StringComparison.OrdinalIgnoreCase) &&
                filePath.Length > twinId.Length &&
                filePath[twinId.Length] == '/')
            {
                return filePath.Substring(twinId.Length + 1);
            }

            return filePath;
        }

        /// <summary>
        /// Delete a complete document and all its chapters by FileName
        /// Deletes all documents in the index that have the specified FileName value
        /// </summary>
        /// <param name="fileName">FileName to delete - all documents with this FileName will be deleted</param>
        /// <param name="twinId">Optional TwinID to restrict deletion to specific Twin (if null, deletes across all twins)</param>
        /// <returns>Result with deletion statistics and any errors</returns>
        public async Task<NoStructuredDeleteResult> DeleteDocumentByDocumentIdAsync(string fileName, string? twinId = null)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredDeleteResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        DocumentId = fileName
                    };
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    return new NoStructuredDeleteResult
                    {
                        Success = false,
                        Error = "FileName cannot be null or empty",
                        DocumentId = fileName
                    };
                }

                _logger.LogInformation("🗑️ Starting deletion of documents with FileName: {FileName}, TwinID: {TwinId}",
                    fileName, twinId ?? "*");

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // STEP 1: Search for all documents with the specified FileName
                var searchOptions = new SearchOptions
                {
                    Size = 1000, // Get up to 1000 documents at a time
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Simple
                };

                // Build filter for FileName and optionally TwinID
                var filterParts = new List<string>
                {
                    $"FileName eq '{fileName.Replace("'", "''")}'"
                };

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                searchOptions.Filter = string.Join(" and ", filterParts);

                // Select only the ID field since we just need to delete
                searchOptions.Select.Add("id");

                _logger.LogInformation("🔍 Searching for documents to delete with filter: {Filter}", searchOptions.Filter);

                var searchResponse = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
                var documentsToDelete = new List<string>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var documentId = result.Document.GetString("id");
                    if (!string.IsNullOrEmpty(documentId))
                    {
                        documentsToDelete.Add(documentId);
                    }
                }

                var totalFound = (int)(searchResponse.Value.TotalCount ?? 0);
                _logger.LogInformation("📊 Found {DocumentCount} documents to delete for FileName: {FileName}",
                    documentsToDelete.Count, fileName);

                if (documentsToDelete.Count == 0)
                {
                    return new NoStructuredDeleteResult
                    {
                        Success = true,
                        DocumentId = fileName,
                        DeletedChaptersCount = 0,
                        TotalChaptersFound = 0,
                        Message = $"No documents found with FileName '{fileName}'" +
                                 (string.IsNullOrEmpty(twinId) ? "" : $" for TwinID '{twinId}'")
                    };
                }

                // STEP 2: Delete documents in batches
                var deleteErrors = new List<string>();
                var totalDeleted = 0;
                const int batchSize = 100; // Azure Search batch limit

                for (int i = 0; i < documentsToDelete.Count; i += batchSize)
                {
                    var batch = documentsToDelete.Skip(i).Take(batchSize).ToList();

                    try
                    {
                        _logger.LogInformation("🗑️ Deleting batch {BatchNumber}: {BatchSize} documents",
                            (i / batchSize) + 1, batch.Count);

                        // Create batch of delete actions
                        var deleteActions = batch.Select(id => IndexDocumentsAction.Delete("id", id)).ToArray();
                        var batchOperation = IndexDocumentsBatch.Create(deleteActions);

                        // Execute batch delete
                        var deleteResponse = await searchClient.IndexDocumentsAsync(batchOperation);

                        // Check for errors in this batch
                        var batchErrors = deleteResponse.Value.Results
                            .Where(r => !r.Succeeded)
                            .Select(r => $"Document ID '{r.Key}': {r.ErrorMessage}")
                            .ToList();

                        if (batchErrors.Any())
                        {
                            deleteErrors.AddRange(batchErrors);
                            _logger.LogWarning("⚠️ Batch {BatchNumber} completed with {ErrorCount} errors",
                                (i / batchSize) + 1, batchErrors.Count);
                        }

                        // Count successful deletions in this batch
                        var batchSuccessCount = deleteResponse.Value.Results.Count(r => r.Succeeded);
                        totalDeleted += batchSuccessCount;

                        _logger.LogInformation("✅ Batch {BatchNumber} completed: {SuccessCount}/{BatchSize} documents deleted",
                            (i / batchSize) + 1, batchSuccessCount, batch.Count);
                    }
                    catch (Exception batchEx)
                    {
                        var errorMessage = $"Batch {(i / batchSize) + 1} failed: {batchEx.Message}";
                        deleteErrors.Add(errorMessage);
                        _logger.LogError(batchEx, "❌ Error deleting batch {BatchNumber}", (i / batchSize) + 1);
                    }
                }

                var finalMessage = $"Deleted {totalDeleted} of {documentsToDelete.Count} documents with FileName '{fileName}'" +
                                  (string.IsNullOrEmpty(twinId) ? "" : $" for TwinID '{twinId}'");

                if (deleteErrors.Any())
                {
                    finalMessage += $". {deleteErrors.Count} errors occurred during deletion.";
                }

                _logger.LogInformation("🏁 Deletion completed: {Message}", finalMessage);

                return new NoStructuredDeleteResult
                {
                    Success = deleteErrors.Count == 0, // Success only if no errors occurred
                    DocumentId = fileName,
                    DeletedChaptersCount = totalDeleted,
                    TotalChaptersFound = totalFound,
                    Message = finalMessage,
                    Errors = deleteErrors,
                    Error = deleteErrors.Any() ? $"Deletion completed with {deleteErrors.Count} errors. See Errors list for details." : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting documents with FileName: {FileName}", fileName);
                return new NoStructuredDeleteResult
                {
                    Success = false,
                    Error = $"Error deleting documents: {ex.Message}",
                    DocumentId = fileName,
                    DeletedChaptersCount = 0,
                    TotalChaptersFound = 0,
                    Errors = new List<string> { ex.Message }
                };
            }
        }

        /// <summary>
        /// Index multiple CapituloDocumento from a document processing result
        /// </summary>
        public async Task<List<NoStructuredIndexResult>> IndexMultipleCapitulosAsync(List<CapituloDocumento> capitulos,
            string subcategoria,
            string twinID)
        {
            return new List<NoStructuredIndexResult>
            {
                new NoStructuredIndexResult
                {
                    Success = false,
                    Error = "Method not implemented - simplified version for UploadDocumentTOIndex functionality only"
                }
            };
        }

        /// <summary>
        /// Generate embeddings using Azure OpenAI
        /// </summary>
        private async Task<float[]?> GenerateEmbeddingsAsync(string text)
        {
            try
            {
                if (_embeddingClient == null || string.IsNullOrEmpty(text))
                {
                    return null;
                }

                _logger.LogDebug("🤖 Generating embeddings for text: {Length} characters", text.Length);

                // Truncate text if too long for embeddings
                if (text.Length > 8000)
                {
                    text = text.Substring(0, 8000);
                    _logger.LogDebug("✂️ Text truncated to 8000 characters for embedding generation");
                }

                // Check if the model supports custom dimensions
                bool supportsCustomDimensions = !string.IsNullOrEmpty(_embeddingDeployment) &&
                                              (_embeddingDeployment.Contains("text-embedding-3") ||
                                               _embeddingDeployment.Contains("text-embedding-ada-003"));

                EmbeddingGenerationOptions? embeddingOptions = null;

                // FIXED: Always use the same dimensions as defined in EmbeddingDimensions field
                // This ensures consistency between index creation and embedding generation
                if (supportsCustomDimensions)
                {
                    embeddingOptions = new EmbeddingGenerationOptions
                    {
                        Dimensions = EmbeddingDimensions  // Use the same dimensions as the index (1536 or 3072)
                    };
                    _logger.LogDebug("📐 Using custom dimensions: {Dimensions} for model: {Model}", EmbeddingDimensions, _embeddingDeployment);
                }
                else
                {
                    _logger.LogDebug("📐 Using default dimensions for model: {Model} (does not support custom dimensions)", _embeddingDeployment ?? "default");
                }

                // Generate embeddings with or without dimensions based on model support
                var embedding = embeddingOptions != null
                    ? await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions)
                    : await _embeddingClient.GenerateEmbeddingAsync(text);

                var embeddings = embedding.Value.ToFloats().ToArray();

                _logger.LogDebug("✅ Generated embeddings: {ActualDimensions} dimensions (expected: {ExpectedDimensions})",
                    embeddings.Length, EmbeddingDimensions);

                // Validate dimensions match expectation
                if (embeddings.Length != EmbeddingDimensions)
                {
                    _logger.LogWarning("⚠️ Dimension mismatch: generated {ActualDimensions} but expected {ExpectedDimensions}. " +
                                     "This may cause indexing errors.", embeddings.Length, EmbeddingDimensions);
                }

                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error generating embeddings, continuing without vector search");
                return null;
            }
        }

        /// <summary>
        /// Build complete content for vector search by combining all relevant fields from ExractedChapterSubsIndex
        /// </summary>
        private static string BuildCompleteContent(ExractedChapterSubsIndex chapterSubsIndex)
        {
            var content = new List<string>();

            // Document information
            if (!string.IsNullOrEmpty(chapterSubsIndex.FileName))
                content.Add($"Archivo: {chapterSubsIndex.FileName}");

            if (!string.IsNullOrEmpty(chapterSubsIndex.FilePath))
                content.Add($"Ruta: {chapterSubsIndex.FilePath}");

            // Chapter information
            if (!string.IsNullOrEmpty(chapterSubsIndex.ChapterTitle))
                content.Add($"Capítulo: {chapterSubsIndex.ChapterTitle}");

            if (!string.IsNullOrEmpty(chapterSubsIndex.TextChapter))
                content.Add($"Contenido del capítulo: {chapterSubsIndex.TextChapter}");

            content.Add($"Páginas del capítulo: {chapterSubsIndex.FromPageChapter} - {chapterSubsIndex.ToPageChapter}");

            // Subchapter information
            if (!string.IsNullOrEmpty(chapterSubsIndex.TitleSub))
                content.Add($"Subcapítulo: {chapterSubsIndex.TitleSub}");

            if (!string.IsNullOrEmpty(chapterSubsIndex.TextSub))
                content.Add($"Contenido del subcapítulo: {chapterSubsIndex.TextSub}");

            content.Add($"Páginas del subcapítulo: {chapterSubsIndex.FromPageSub} - {chapterSubsIndex.ToPageSub}");

            // Metadata
            content.Add($"Tokens del documento: {chapterSubsIndex.TotalTokensDocument}");
            content.Add($"Tokens del capítulo: {chapterSubsIndex.TotalTokens}");
            content.Add($"Tokens del subcapítulo: {chapterSubsIndex.TotalTokensSub}");

            return string.Join(". ", content);
        }

        /// <summary>
        /// Create the no-structured documents search index with vector and semantic search capabilities
        /// </summary>
        public async Task<NoStructuredIndexResult> CreateNoStructuredIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new NoStructuredIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📄 Creating No-Structured Documents Search Index: {IndexName}", IndexName);

                // Define search fields based on the ExractedChapterSubsIndex class structure
                var fields = new List<SearchField>
                {
                    // Primary identification field
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },
                     new SimpleField("Subcategoria", SearchFieldDataType.String)
                    {

                        IsFilterable = true,
                        IsSortable = true
                    },
                    // Chapter identification from ExractedChapterSubsIndex
                    new SearchableField("ChapterTitle")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // TwinID from ExractedChapterSubsIndex
                    new SearchableField("TwinID")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },
                    
                    // ChapterID from ExractedChapterSubsIndex
                    new SearchableField("ChapterID")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },
                    
                    // Document metadata from ExractedChapterSubsIndex
                    new SimpleField("TotalTokensDocument", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // File information from ExractedChapterSubsIndex
                    new SearchableField("FileName")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    new SearchableField("FilePath")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },
                    
                    // Chapter content from ExractedChapterSubsIndex
                    new SearchableField("TextChapter")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Chapter page range from ExractedChapterSubsIndex
                    new SimpleField("FromPageChapter", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    new SimpleField("ToPageChapter", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Chapter total tokens from ExractedChapterSubsIndex
                    new SimpleField("TotalTokens", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Subchapter information from ExractedChapterSubsIndex
                    new SearchableField("TitleSub")
                    {
                        IsFilterable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Subchapter content from ExractedChapterSubsIndex (main searchable content)
                    new SearchableField("TextSub")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Subchapter token count from ExractedChapterSubsIndex
                    new SimpleField("TotalTokensSub", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Subchapter page range from ExractedChapterSubsIndex
                    new SimpleField("FromPageSub", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    new SimpleField("ToPageSub", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },
                    
                    // Processing timestamp
                    new SimpleField("DateCreated", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },
                    
                    // Combined content field for comprehensive search
                    new SearchableField("ContenidoCompleto")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },
                    
                    // Vector field for semantic similarity search
                    new SearchField("ContenidoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = EmbeddingDimensions,
                        VectorSearchProfileName = VectorSearchProfile
                    }
                };

                // Configure vector search
                var vectorSearch = new VectorSearch();

                // Add HNSW algorithm configuration
                vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(HnswAlgorithmConfig));

                // Add vector search profile
                vectorSearch.Profiles.Add(new VectorSearchProfile(VectorSearchProfile, HnswAlgorithmConfig));

                // Configure semantic search
                var semanticSearch = new SemanticSearch();
                var prioritizedFields = new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("TitleSub")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("TextSub"));
                prioritizedFields.ContentFields.Add(new SemanticField("TextChapter"));
                prioritizedFields.ContentFields.Add(new SemanticField("ContenidoCompleto"));
                prioritizedFields.ContentFields.Add(new SemanticField("ChapterTitle"));

                // Keywords fields for semantic ranking
                prioritizedFields.KeywordsFields.Add(new SemanticField("ChapterID"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("TwinID"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("FileName"));

                semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

                // Create the no-structured documents search index
                var index = new SearchIndex(IndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("✅ No-Structured Documents Index '{IndexName}' created successfully", IndexName);

                return new NoStructuredIndexResult
                {
                    Success = true,
                    Message = $"No-Structured Documents Index '{IndexName}' created successfully",
                    IndexName = IndexName,
                    FieldsCount = fields.Count,
                    HasVectorSearch = true,
                    HasSemanticSearch = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating No-Structured Documents Index");
                return new NoStructuredIndexResult
                {
                    Success = false,
                    Error = $"Error creating No-Structured Documents Index: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Search and return ExractedChapterSubsIndex list directly by TwinID and FileName
        /// </summary>
        /// <param name="twinId">Twin ID to filter by</param>
        /// <param name="fileName">FileName to filter by (optional, use "*" or empty for all files)</param>
        /// <param name="searchQuery">Optional search query</param>
        /// <param name="top">Maximum number of results</param>
        /// <returns>List of ExractedChapterSubsIndex matching the criteria</returns>
        public async Task<List<ExractedChapterSubsIndex>> SearchExractedChaptersByTwinIdAndFileNameAsync(
            string twinId,
            string? fileName = null,
            string? searchQuery = null,
            int top = 1000)
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.LogWarning("⚠️ Azure Search service not available");
                    return new List<ExractedChapterSubsIndex>();
                }

                _logger.LogInformation("📄 Searching ExractedChapterSubsIndex by TwinID: {TwinId}, FileName: {FileName}", twinId, fileName);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Size = top,
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Simple
                };

                // Build filter for TwinID and optionally by FileName
                var filterParts = new List<string>();

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                // Add FileName filter if provided and not wildcard
                if (!string.IsNullOrEmpty(fileName) && fileName != "*")
                {
                    filterParts.Add($"FileName eq '{fileName.Replace("'", "''")}'");
                }

                if (filterParts.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filterParts);
                }

                // Select all fields for complete ExractedChapterSubsIndex objects
                var fieldsToSelect = new[]
                {
                    "id", "ChapterTitle", "TwinID", "ChapterID", "TotalTokensDocument",
                    "FileName", "FilePath", "TextChapter", "FromPageChapter", "ToPageChapter",
                    "TotalTokens", "TitleSub", "TextSub", "TotalTokensSub", "FromPageSub",
                    "ToPageSub", "DateCreated"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // Use search query if provided, otherwise use "*" to get all matching documents
                var searchText = !string.IsNullOrEmpty(searchQuery) ? searchQuery : "*";

                var searchResponse = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);
                var searchResults = new List<ExractedChapterSubsIndex>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new ExractedChapterSubsIndex
                    {
                        id = result.Document.GetString("id") ?? string.Empty,
                        ChapterTitle = result.Document.GetString("ChapterTitle") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        ChapterID = result.Document.GetString("ChapterID") ?? string.Empty,
                        TotalTokensDocument = result.Document.GetInt32("TotalTokensDocument") ?? 0,
                        FileName = result.Document.GetString("FileName") ?? string.Empty,
                        FilePath = result.Document.GetString("FilePath") ?? string.Empty,

                        // Chapter fields with full content
                        TextChapter = result.Document.GetString("TextChapter") ?? string.Empty,
                        FromPageChapter = result.Document.GetInt32("FromPageChapter") ?? 0,
                        ToPageChapter = result.Document.GetInt32("ToPageChapter") ?? 0,
                        TotalTokens = result.Document.GetInt32("TotalTokens") ?? 0,

                        // Subchapter fields with full content
                        TitleSub = result.Document.GetString("TitleSub") ?? string.Empty,
                        TextSub = result.Document.GetString("TextSub") ?? string.Empty,
                        TotalTokensSub = result.Document.GetInt32("TotalTokensSub") ?? 0,
                        FromPageSub = result.Document.GetInt32("FromPageSub") ?? 0,
                        ToPageSub = result.Document.GetInt32("ToPageSub") ?? 0
                    };

                    searchResults.Add(chapterItem);
                }

                _logger.LogInformation("✅ Found {ChapterCount} ExractedChapterSubsIndex for TwinID: {TwinId}, FileName: {FileName}",
                    searchResults.Count, twinId, fileName ?? "*");

                return searchResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching ExractedChapterSubsIndex by TwinID: {TwinId}, FileName: {FileName}", twinId, fileName);
                return new List<ExractedChapterSubsIndex>();
            }
        }

        /// <summary>
        /// Busca contenido relevante usando búsqueda semántica y vectorial basada en una pregunta del usuario
        /// Retorna máximo 5 resultados con todos los campos necesarios para el análisis
        /// </summary>
        /// <param name="question">Pregunta del usuario</param>
        /// <param name="twinId">Twin ID para filtrar resultados</param>
        /// <param name="fileName">Nombre del archivo para filtrar resultados (opcional)</param>
        /// <returns>Lista de ExractedChapterSubsIndex con contenido relevante</returns>
        public async Task<List<ExractedChapterSubsIndex>> AnswerSearchUserQuestionAsync(string question,
            string twinId, string? fileName = null)
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.LogWarning("⚠️ Azure Search service not available");
                    return new List<ExractedChapterSubsIndex>();
                }

                if (string.IsNullOrEmpty(question) || string.IsNullOrEmpty(twinId))
                {
                    _logger.LogWarning("⚠️ Question or TwinID cannot be null or empty");
                    return new List<ExractedChapterSubsIndex>();
                }

                _logger.LogInformation("🔍 Searching relevant content for question: {Question}, TwinID: {TwinId}, FileName: {FileName}",
                    question, twinId, fileName ?? "*");

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // PASO 1: Generar embeddings para la pregunta del usuario
                _logger.LogInformation("🤖 STEP 1: Generating embeddings for user question...");
                float[]? questionEmbeddings = null;
                if (_embeddingClient != null)
                {
                    questionEmbeddings = await GenerateEmbeddingsAsync(question);
                }

                var searchOptions = new SearchOptions
                {
                    Size = 5, // Máximo 5 resultados como se solicitó
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Semantic // Usar búsqueda semántica
                };

                // PASO 2: Configurar filtros
                var filterParts = new List<string>();

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                if (fileName != "Global")
                {
                    filterParts.Add($"FileName eq '{fileName.Replace("'", "''")}'");
                }

                if (filterParts.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filterParts);
                }

                // PASO 3: Seleccionar todos los campos necesarios
                var fieldsToSelect = new[]
                {
                    "id", "ChapterTitle", "TwinID", "ChapterID", "TotalTokensDocument",
                    "FileName", "FilePath", "TextChapter", "FromPageChapter", "ToPageChapter",
                    "TotalTokens", "TitleSub", "TextSub", "TotalTokensSub", "FromPageSub",
                    "ToPageSub", "DateCreated", "Subcategoria"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // PASO 4: Configurar búsqueda semántica y vectorial
                searchOptions.SemanticSearch = new()
                {
                    SemanticConfigurationName = SemanticConfig,
                    QueryCaption = new(QueryCaptionType.Extractive),
                    QueryAnswer = new(QueryAnswerType.Extractive)
                };

                // PASO 5: Configurar búsqueda vectorial si tenemos embeddings
                if (questionEmbeddings != null)
                {
                    _logger.LogInformation("🧭 STEP 2: Configuring vector search with generated embeddings...");

                    searchOptions.VectorSearch = new()
                    {
                        Queries =
                        {
                            new VectorizedQuery(questionEmbeddings.ToArray())
                            {
                                KNearestNeighborsCount = 5,
                                Fields = { "ContenidoVector" }
                            }
                        }
                    };
                }

                // PASO 6: Ejecutar búsqueda híbrida (semántica + vectorial + texto)
                _logger.LogInformation("🔍 STEP 3: Executing hybrid search (semantic + vector + text)...");

                var searchResponse = await searchClient.SearchAsync<SearchDocument>(question, searchOptions);
                var searchResults = new List<ExractedChapterSubsIndex>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new ExractedChapterSubsIndex
                    {
                        id = result.Document.GetString("id") ?? string.Empty,
                        ChapterTitle = result.Document.GetString("ChapterTitle") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        ChapterID = result.Document.GetString("ChapterID") ?? string.Empty,
                        TotalTokensDocument = result.Document.GetInt32("TotalTokensDocument") ?? 0,
                        FileName = result.Document.GetString("FileName") ?? string.Empty,
                        FilePath = result.Document.GetString("FilePath") ?? string.Empty,
                        Subcategoria = result.Document.GetString("Subcategoria") ?? string.Empty,

                        // Campos del capítulo con contenido completo
                        TextChapter = result.Document.GetString("TextChapter") ?? string.Empty,
                        FromPageChapter = result.Document.GetInt32("FromPageChapter") ?? 0,
                        ToPageChapter = result.Document.GetInt32("ToPageChapter") ?? 0,
                        TotalTokens = result.Document.GetInt32("TotalTokens") ?? 0,

                        // Campos del subcapítulo con contenido completo
                        TitleSub = result.Document.GetString("TitleSub") ?? string.Empty,
                        TextSub = result.Document.GetString("TextSub") ?? string.Empty,
                        TotalTokensSub = result.Document.GetInt32("TotalTokensSub") ?? 0,
                        FromPageSub = result.Document.GetInt32("FromPageSub") ?? 0,
                        ToPageSub = result.Document.GetInt32("ToPageSub") ?? 0
                    };

                    searchResults.Add(chapterItem);
                }

                // PASO 7: Ordenar por relevancia (los resultados ya vienen ordenados por score)
                _logger.LogInformation("✅ Hybrid search completed: Found {ResultCount} relevant chapters for question",
                    searchResults.Count);

                // Log de información de depuración
                foreach (var result in searchResults.Take(3)) // Solo los primeros 3 para el log
                {
                    var contentPreview = !string.IsNullOrEmpty(result.TextSub)
                        ? result.TextSub.Length > 100 ? result.TextSub.Substring(0, 100) + "..." : result.TextSub
                        : result.TextChapter.Length > 100 ? result.TextChapter.Substring(0, 100) + "..." : result.TextChapter;

                    _logger.LogInformation("📄 Relevant result: {Title} ({SubTitle}) - Preview: {Preview}",
                        result.ChapterTitle,
                        !string.IsNullOrEmpty(result.TitleSub) ? result.TitleSub : "No subcapítulo",
                        contentPreview);
                }

                return searchResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching relevant content for question: {Question}, TwinID: {TwinId}, FileName: {FileName}",
                    question, twinId, fileName);
                return new List<ExractedChapterSubsIndex>();
            }
        }
    

            /// <summary>
        /// Busca contenido relevante en TODOS los documentos usando búsqueda semántica y vectorial basada en una pregunta del usuario
        /// Retorna máximo 5 resultados con todos los campos necesarios para el análisis
        /// Similar a AnswerSearchUserQuestionAsync pero busca en todos los documentos del Twin (sin filtro de FileName)
        /// </summary>
        /// <param name="question">Pregunta del usuario</param>
        /// <param name="twinId">Twin ID para filtrar resultados</param>
        /// <returns>Lista de ExractedChapterSubsIndex con contenido relevante de todos los documentos</returns>
        public async Task<List<ExractedChapterSubsIndex>> AnswerSearchUserQuestionAllDocumentsAsync(string question,
            string twinId)
        {
            try
            {
                if (!IsAvailable)
                {
                    _logger.LogWarning("⚠️ Azure Search service not available");
                    return new List<ExractedChapterSubsIndex>();
                }

                if (string.IsNullOrEmpty(question) || string.IsNullOrEmpty(twinId))
                {
                    _logger.LogWarning("⚠️ Question or TwinID cannot be null or empty");
                    return new List<ExractedChapterSubsIndex>();
                }

                _logger.LogInformation("🔍 Searching relevant content across ALL documents for question: {Question}, TwinID: {TwinId}",
                    question, twinId);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // PASO 1: Generar embeddings para la pregunta del usuario
                _logger.LogInformation("🤖 STEP 1: Generating embeddings for user question...");
                float[]? questionEmbeddings = null;
                if (_embeddingClient != null)
                {
                    questionEmbeddings = await GenerateEmbeddingsAsync(question);
                }

                var searchOptions = new SearchOptions
                {
                    Size = 5, // Máximo 5 resultados como se solicitó
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Semantic // Usar búsqueda semántica
                };

                // PASO 2: Configurar filtros - SOLO TwinID, sin FileName
                var filterParts = new List<string>();

                if (!string.IsNullOrEmpty(twinId))
                {
                    filterParts.Add($"TwinID eq '{twinId.Replace("'", "''")}'");
                }

                if (filterParts.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filterParts);
                }

                // PASO 3: Seleccionar todos los campos necesarios
                var fieldsToSelect = new[]
                {
                    "id", "ChapterTitle", "TwinID", "ChapterID", "TotalTokensDocument",
                    "FileName", "FilePath", "TextChapter", "FromPageChapter", "ToPageChapter",
                    "TotalTokens", "TitleSub", "TextSub", "TotalTokensSub", "FromPageSub",
                    "ToPageSub", "DateCreated", "Subcategoria"
                };
                foreach (var field in fieldsToSelect)
                {
                    searchOptions.Select.Add(field);
                }

                // PASO 4: Configurar búsqueda semántica y vectorial
                searchOptions.SemanticSearch = new()
                {
                    SemanticConfigurationName = SemanticConfig,
                    QueryCaption = new(QueryCaptionType.Extractive),
                    QueryAnswer = new(QueryAnswerType.Extractive)
                };

                // PASO 5: Configurar búsqueda vectorial si tenemos embeddings
                if (questionEmbeddings != null)
                {
                    _logger.LogInformation("🧭 STEP 2: Configuring vector search with generated embeddings...");

                    searchOptions.VectorSearch = new()
                    {
                        Queries =
                        {
                            new VectorizedQuery(questionEmbeddings.ToArray())
                            {
                                KNearestNeighborsCount = 5,
                                Fields = { "ContenidoVector" }
                            }
                        }
                    };
                }

                // PASO 6: Ejecutar búsqueda híbrida (semántica + vectorial + texto) en TODOS los documentos
                _logger.LogInformation("🔍 STEP 3: Executing hybrid search across ALL documents (semantic + vector + text)...");

                var searchResponse = await searchClient.SearchAsync<SearchDocument>(question, searchOptions);
                var searchResults = new List<ExractedChapterSubsIndex>();

                await foreach (var result in searchResponse.Value.GetResultsAsync())
                {
                    var chapterItem = new ExractedChapterSubsIndex
                    {
                        id = result.Document.GetString("id") ?? string.Empty,
                        ChapterTitle = result.Document.GetString("ChapterTitle") ?? string.Empty,
                        TwinID = result.Document.GetString("TwinID") ?? string.Empty,
                        ChapterID = result.Document.GetString("ChapterID") ?? string.Empty,
                        TotalTokensDocument = result.Document.GetInt32("TotalTokensDocument") ?? 0,
                        FileName = result.Document.GetString("FileName") ?? string.Empty,
                        FilePath = result.Document.GetString("FilePath") ?? string.Empty,
                        Subcategoria = result.Document.GetString("Subcategoria") ?? string.Empty,

                        // Campos del capítulo con contenido completo
                        TextChapter = result.Document.GetString("TextChapter") ?? string.Empty,
                        FromPageChapter = result.Document.GetInt32("FromPageChapter") ?? 0,
                        ToPageChapter = result.Document.GetInt32("ToPageChapter") ?? 0,
                        TotalTokens = result.Document.GetInt32("TotalTokens") ?? 0,

                        // Campos del subcapítulo con contenido completo
                        TitleSub = result.Document.GetString("TitleSub") ?? string.Empty,
                        TextSub = result.Document.GetString("TextSub") ?? string.Empty,
                        TotalTokensSub = result.Document.GetInt32("TotalTokensSub") ?? 0,
                        FromPageSub = result.Document.GetInt32("FromPageSub") ?? 0,
                        ToPageSub = result.Document.GetInt32("ToPageSub") ?? 0
                    };

                    searchResults.Add(chapterItem);
                }

                // PASO 7: Ordenar por relevancia (los resultados ya vienen ordenados por score)
                _logger.LogInformation("✅ Hybrid search completed across ALL documents: Found {ResultCount} relevant chapters for question",
                    searchResults.Count);

                // Log de información de depuración
                foreach (var result in searchResults.Take(3)) // Solo los primeros 3 para el log
                {
                    var contentPreview = !string.IsNullOrEmpty(result.TextSub)
                        ? result.TextSub.Length > 100 ? result.TextSub.Substring(0, 100) + "..." : result.TextSub
                        : result.TextChapter.Length > 100 ? result.TextChapter.Substring(0, 100) + "..." : result.TextChapter;

                    _logger.LogInformation("📄 Relevant result from {FileName}: {Title} ({SubTitle}) - Preview: {Preview}",
                        result.FileName,
                        result.ChapterTitle,
                        !string.IsNullOrEmpty(result.TitleSub) ? result.TitleSub : "No subcapítulo",
                        contentPreview);
                }

                return searchResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching relevant content across ALL documents for question: {Question}, TwinID: {TwinId}",
                    question, twinId);
                return new List<ExractedChapterSubsIndex>();
            }
        }
    }

    /// <summary>
    /// Result class for no-structured documents index operations
    /// </summary>
    public class NoStructuredIndexResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? IndexName { get; set; }
        public string? DocumentId { get; set; }
        public int FieldsCount { get; set; }
        public bool HasVectorSearch { get; set; }
        public bool HasSemanticSearch { get; set; }
    }

    /// <summary>
    /// Search result class for no-structured documents
    /// </summary>
    public class NoStructuredSearchResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<NoStructuredDocument> Documents { get; set; } = new();
        public long TotalChapters { get; set; }
        public int TotalDocuments { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public string SearchType { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    /// <summary>
    /// Search result class for no-structured documents metadata
    /// </summary>
    public class NoStructuredSearchMetadataResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<NoStructuredDocumentMetadata> Documents { get; set; } = new();
        public long TotalChapters { get; set; }
        public int TotalDocuments { get; set; }
        public string SearchQuery { get; set; } = string.Empty;
        public string SearchType { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    /// <summary>
    /// Document summary that groups chapters by DocumentID
    /// </summary>
    public class NoStructuredDocument
    {
        public string DocumentID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string Estructura { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public int TotalChapters { get; set; }
        public int TotalTokens { get; set; }
        public int TotalPages { get; set; }
        public DateTimeOffset ProcessedAt { get; set; }
        public double SearchScore { get; set; }
        public List<NoStructuredSearchResultItem> Capitulos { get; set; } = new();
        public string DocumentTitle => $"Documento {Estructura} - {Subcategoria}";
    }

    /// <summary>
    /// Document metadata summary without chapters content
    /// </summary>
    public class NoStructuredDocumentMetadata
    {
        public string DocumentID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string Estructura { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public int TotalChapters { get; set; }
        public int TotalTokens { get; set; }
        public int TotalPages { get; set; }
        public DateTimeOffset ProcessedAt { get; set; }
        public double SearchScore { get; set; }
    }

    /// <summary>
    /// Individual search result item for no-structured documents
    /// </summary>
    public class NoStructuredSearchResultItem
    {
        public string Id { get; set; } = string.Empty;
        public string DocumentID { get; set; } = string.Empty;
        public string CapituloID { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public double SearchScore { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string NumeroCapitulo { get; set; } = string.Empty;
        public int PaginaDe { get; set; }
        public int PaginaA { get; set; }
        public int Nivel { get; set; }
        public int TotalTokens { get; set; }
    }

    /// <summary>
    /// Result class for document deletion operations
    /// </summary>
    public class NoStructuredDeleteResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string DocumentId { get; set; } = string.Empty;
        public int DeletedChaptersCount { get; set; }
        public int TotalChaptersFound { get; set; }
        public string? Message { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// New structure for chapter and subchapter indexing in no-structured documents
    /// </summary>
    public class ExractedChapterSubsIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("chapter")]
        public string ChapterTitle { get; set; } = string.Empty;

        public string id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;

        public string Subcategoria { get; set; } = string.Empty;
        public int TotalTokensDocument { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ChapterID { get; set; } = string.Empty;
        public string TextChapter { get; set; } = string.Empty;
        public int FromPageChapter { get; set; }
        public int ToPageChapter { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }

        public string TitleSub { get; set; } = string.Empty;
        public string TextSub { get; set; } = string.Empty;
        public int TotalTokensSub { get; set; }
        public int FromPageSub { get; set; }
        public int ToPageSub { get; set; }

        public string fileURL { get; set; } = string.Empty;

        public float[]? ContenidoVector { get; set; } = null;
    }
     
}