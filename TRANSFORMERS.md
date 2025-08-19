# DB2XL Transformer System Guide

A comprehensive guide to the advanced data transformation system in DB2XL, enabling human-readable output from raw SQLite data while maintaining fidelity.

> **🎯 Goal**: Transform opaque database values (epoch timestamps, JSON blobs, binary data) into human-readable formats without losing data fidelity.

---

## 🚀 Quick Start

### Basic Example

```csharp
using DB2XL.Transformers;
using DB2XL.Configuration;

// Create a basic transformer registry
var registry = new TransformerRegistryBuilder()
    .AddTextTransformers()
    .AddTimeTransformers() 
    .AddJsonTransformers()
    .Build();

// Transform a Unix timestamp
var epochTransformer = registry.CreateCell("epoch", new Dictionary<string, string>
{
    ["unit"] = "ms",
    ["format"] = "yyyy-MM-dd HH:mm:ss",
    ["tz"] = "UTC"
});

var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);
var result = epochTransformer.Transform(context, "1692100856000");
// Result: "2023-08-15 12:00:56"
```

### Configuration-Driven Transformations

```json
{
  "version": "1.0",
  "global": {
    "enableTransformations": true,
    "errorHandling": "LogAndContinue",
    "performance": {
      "batchSize": 10000,
      "enableParallelProcessing": true
    }
  },
  "tables": {
    "events": {
      "columns": {
        "timestamp": [
          {
            "name": "epoch",
            "config": {
              "unit": "ms",
              "format": "yyyy-MM-dd HH:mm:ss",
              "tz": "UTC"
            }
          }
        ],
        "payload": [
          {
            "name": "json-pretty",
            "config": {
              "indent": "  ",
              "maxDepth": "5"
            }
          }
        ]
      }
    }
  }
}
```

---

## 📊 Built-in Transformer Library

### 🔤 Text Transformers

#### UpperCaseTransformer
Converts text to uppercase with culture support.

```csharp
// Configuration
{
  "culture": "invariant|current|turkish",  // Default: "invariant"
  "forceApply": "true|false"               // Default: false
}

// Auto-applies to columns containing: "name", "title", "text"
// Example: "john doe" → "JOHN DOE"
```

#### LowerCaseTransformer  
Converts text to lowercase with culture support.

```csharp
// Configuration
{
  "culture": "invariant|current|turkish",  // Default: "invariant"
  "forceApply": "true|false"               // Default: false
}

// Example: "JOHN DOE" → "john doe"
```

#### TitleCaseTransformer
Converts text to proper title case.

```csharp
// Configuration  
{
  "culture": "invariant|current",  // Default: "current"
  "forceApply": "true|false"       // Default: false
}

// Auto-applies to columns containing: "name", "title"
// Example: "john doe smith" → "John Doe Smith"
```

#### TrimTransformer
Removes whitespace or custom characters.

```csharp
// Configuration
{
  "mode": "both|start|end",     // Default: "both"
  "chars": "characters_to_trim", // Default: whitespace
  "forceApply": "true|false"    // Default: false
}

// Example: "  hello world  " → "hello world"
```

#### TruncateTransformer
Limits text length with ellipsis options.

```csharp
// Configuration
{
  "maxLength": "100",           // Default: 100
  "ellipsis": "...",            // Default: "..."
  "mode": "end|middle|start",   // Default: "end"
  "forceApply": "true|false"    // Default: false
}

// Example: "Very long text..." → "Very long tex..."
```

#### CoalesceTransformer
Replaces null or empty values with defaults.

```csharp
// Configuration
{
  "default": "N/A",                  // Default: "N/A"
  "treatEmptyAsNull": "true|false",  // Default: true
  "forceApply": "true|false"         // Default: false
}

// Example: null → "N/A", "" → "N/A"
```

#### RegexReplaceTransformer
Performs pattern-based find and replace.

```csharp
// Configuration
{
  "pattern": "regex_pattern",        // Required
  "replacement": "replacement_text", // Default: ""
  "maxReplacements": "-1",           // Default: -1 (unlimited)
  "ignoreCase": "true|false",        // Default: false
  "multiline": "true|false",         // Default: false
  "singleline": "true|false",        // Default: false
  "forceApply": "true|false"         // Default: false
}

// Example: "\d{3}-\d{2}-\d{4}" → "XXX-XX-XXXX" (SSN masking)
```

#### MaskTransformer
Masks sensitive data (PII) with intelligent detection.

```csharp
// Configuration
{
  "type": "auto|email|phone|card|ssn|custom",  // Default: "auto"
  "maskChar": "*",                             // Default: "*"
  "keepStart": "2",                            // For custom mode
  "keepEnd": "2",                              // For custom mode
  "forceApply": "true|false"                   // Default: false
}

// Auto-applies to columns containing: "email", "phone", "card", "ssn", "password"
// Examples:
// john.doe@example.com → jo*********@example.com
// 555-123-4567 → 555***4567  
// 4532-1234-5678-9012 → 4532************9012
```

#### NormalizeWhitespaceTransformer
Normalizes whitespace (multiple spaces, tabs, newlines).

```csharp
// Configuration
{
  "replacement": " ",           // Default: single space
  "trim": "true|false",         // Default: true
  "forceApply": "true|false"    // Default: false
}

// Auto-applies to columns containing: "description", "comment", "text"
// Example: "Hello\n\n  world\t!" → "Hello world!"
```

#### SanitizeTransformer
Removes or replaces special characters for safe usage.

```csharp
// Configuration
{
  "mode": "filename|url|alphanumeric|custom",  // Default: "filename"
  "replacement": "-",                          // Default: "-"  
  "removeAccents": "true|false",               // Default: false
  "allowedChars": "a-zA-Z0-9",                // For custom mode
  "forceApply": "true|false"                   // Default: false
}

// Auto-applies to columns containing: "filename", "slug", "url"
// Example: "My File (1).txt" → "My-File--1-.txt"
```

### ⏰ Date/Time Transformers

#### EpochTransformer
Converts Unix timestamps to human-readable dates.

```csharp
// Configuration
{
  "unit": "s|ms|us|ns",                    // Default: "s"
  "format": "yyyy-MM-ddTHH:mm:ssZ",        // Default: ISO 8601
  "tz": "UTC|Local|+05:00|-08:00",        // Default: "UTC"
  "forceApply": "true|false"               // Default: false
}

// Auto-applies to INTEGER columns containing: "time", "date", "epoch"
// Examples:
// 1692100856 (seconds) → "2023-08-15T12:00:56Z"
// 1692100856000 (milliseconds) → "2023-08-15T12:00:56Z"
```

#### TicksTransformer
Converts .NET ticks to human-readable dates.

```csharp
// Configuration
{
  "format": "yyyy-MM-ddTHH:mm:ssZ",    // Default: ISO 8601
  "tz": "UTC|Local|+05:00|-08:00",    // Default: "UTC"
  "forceApply": "true|false"           // Default: false
}

// Auto-applies to INTEGER columns containing: "tick"
// Example: 637589472560000000 → "2021-08-15T12:00:56Z"
```

#### JulianDayTransformer
Converts SQLite Julian Day numbers to dates.

```csharp
// Configuration
{
  "format": "yyyy-MM-ddTHH:mm:ssZ",    // Default: ISO 8601
  "tz": "UTC|Local|+05:00|-08:00",    // Default: "UTC"
  "forceApply": "true|false"           // Default: false
}

// Auto-applies to REAL/INTEGER columns containing: "julian"
// Example: 2459803.0 → "2022-08-15T00:00:00Z"
```

#### DateFormatTransformer
Converts between different date formats and timezones.

```csharp
// Configuration
{
  "inputFormat": "",                       // Default: auto-detect
  "outputFormat": "yyyy-MM-dd HH:mm:ss",   // Default: readable format
  "tz": "UTC|Local|+05:00|-08:00",        // Default: "UTC"
  "forceApply": "true|false"               // Default: false
}

// Auto-applies to TEXT columns containing: "date", "time"
// Example: "2023-08-15T12:00:56Z" → "2023-08-15 12:00:56"
```

#### DatePartTransformer
Extracts specific components from dates.

```csharp
// Configuration
{
  "part": "year|month|day|hour|minute|second|dayofweek|quarter|date|time|iso",
  "inputFormat": "",                   // Default: auto-detect
  "unit": "s|ms",                     // For timestamp inputs
  "forceApply": "true|false"          // Default: false
}

// Auto-applies to TEXT/INTEGER columns containing: "date", "time"
// Examples:
// "2023-08-15T12:30:56Z" + part="year" → "2023"
// "2023-08-15T12:30:56Z" + part="quarter" → "3"
```

### 📄 JSON Transformers

#### JsonCompactTransformer
Minifies JSON by removing whitespace, supports binary formats.

```csharp
// Configuration
{
  "encoding": "auto|base64|hex",     // Default: "auto" for BLOB columns
  "forceApply": "true|false"         // Default: false
}

// Auto-applies to TEXT/BLOB columns containing: "json", "data", "config", "bson"
// Example: Pretty JSON → {"name":"John","age":30}
```

#### JsonPrettyTransformer
Formats JSON with proper indentation.

```csharp
// Configuration
{
  "indent": "  ",                    // Default: two spaces
  "maxDepth": "10",                  // Default: 10
  "forceApply": "true|false"         // Default: false
}

// Auto-applies to TEXT columns containing: "json", "data", "config"
// Example: Compact JSON → Pretty formatted JSON with indentation
```

#### JsonExtractTransformer
Extracts specific values using JSONPath-like syntax.

```csharp
// Configuration
{
  "path": "user.name",               // Required: JSONPath expression
  "default": "",                     // Default: empty string
  "forceApply": "true|false"         // Default: false
}

// Auto-applies to TEXT columns containing: "json", "data"
// Examples:
// {"user":{"name":"John"}} + path="user.name" → "John"
// {"items":[{"id":1}]} + path="items[0].id" → "1"
```

#### JsonFlattenTransformer
Converts nested JSON objects to flat key-value pairs.

```csharp
// Configuration
{
  "separator": ".",                  // Default: "."
  "delimiter": "; ",                 // Default: "; "
  "maxDepth": "5",                   // Default: 5
  "forceApply": "true|false"         // Default: false
}

// Auto-applies to TEXT columns containing: "json", "data"
// Example: {"user":{"name":"John","age":30}} → "user.name=John; user.age=30"
```

#### JsonValidateTransformer
Validates JSON and reports status.

```csharp
// Configuration
{
  "validResult": "VALID",            // Default: "VALID"
  "invalidResult": "INVALID",        // Default: "INVALID"
  "emptyResult": "EMPTY",            // Default: "EMPTY"
  "showError": "true|false",         // Default: false
  "forceApply": "true|false"         // Default: false
}

// Auto-applies to TEXT columns containing: "json", "data"
// Example: Valid JSON → "VALID", Invalid JSON → "INVALID: Unexpected character"
```

#### JsonCountTransformer
Counts elements in JSON structures.

```csharp
// Configuration
{
  "type": "auto|properties|items|all",  // Default: "auto"
  "forceApply": "true|false"             // Default: false
}

// Auto-applies to TEXT columns containing: "json", "data"
// Examples:
// {"a":1,"b":2} + type="properties" → "2"
// [1,2,3,4] + type="items" → "4"
```

### 🔧 Binary/Encoding Transformers

#### BinaryJsonDecodeTransformer
Auto-detects and decodes binary JSON formats.

```csharp
// Configuration
{
  "encoding": "auto|base64|hex",     // Default: "auto"
  "outputFormat": "compact|pretty",  // Default: "compact"
  "forceApply": "true|false"         // Default: false
}

// Auto-applies to BLOB columns containing: "json", "data", "bson"
// Example: Base64 encoded JSON → Decoded and formatted JSON
```

---

## ⚙️ Configuration System

### Global Configuration Structure

```json
{
  "version": "1.0",
  "global": {
    "enableTransformations": true,
    "errorHandling": "LogAndContinue",
    "maxErrors": 100,
    "performance": {
      "batchSize": 10000,
      "enableParallelProcessing": true,
      "maxDegreeOfParallelism": 0
    }
  },
  "globalTransformers": [
    {
      "name": "coalesce",
      "config": {
        "default": "N/A"
      },
      "priority": 1000,
      "enabled": true
    }
  ],
  "tables": {
    "table_name": {
      "enableTransformations": true,
      "columns": {
        "column_name": [
          {
            "name": "transformer_name",
            "config": {
              "param1": "value1",
              "param2": "value2"
            },
            "conditions": {
              "columnPatterns": ["*_time", "*_date"],
              "excludeColumns": ["id", "uuid"],
              "dataTypes": ["integer", "text"],
              "valuePattern": "\\d+"
            },
            "priority": 100,
            "enabled": true
          }
        ]
      },
      "rowTransformers": [
        {
          "name": "row_transformer_name",
          "config": {},
          "priority": 100,
          "enabled": true
        }
      ],
      "filters": {
        "whereClause": "created_at > '2023-01-01'",
        "maxRows": 10000,
        "excludeColumns": ["sensitive_data"],
        "includeColumns": ["id", "name", "timestamp"]
      }
    }
  }
}
```

### Error Handling Strategies

```csharp
public enum ErrorHandling
{
    StopOnError,         // Stop processing on first error
    LogAndContinue,      // Log error and continue processing
    SkipErrors,          // Skip failed transformations silently  
    UseOriginalOnError   // Use original value when transformation fails
}
```

### Pattern Matching

- **Wildcards**: `*` matches any characters, `?` matches single character
- **Column Patterns**: `["*_time", "*_date", "timestamp*"]`
- **Data Types**: `["integer", "real", "text", "blob", "null"]`
- **Value Patterns**: Regular expressions for cell value matching

---

## 🔧 Programming API

### Basic Registry Usage

```csharp
// Create registry builder
var builder = new TransformerRegistryBuilder()
    .AddTextTransformers()
    .AddTimeTransformers()  
    .AddJsonTransformers()
    .AddBinaryTransformers();

// Add custom transformer
builder.Register("custom", config => new MyCustomTransformer(config));

// Build registry
var registry = builder.Build();

// Create transformer instance
var transformer = registry.CreateCell("epoch", new Dictionary<string, string>
{
    ["unit"] = "ms",
    ["format"] = "yyyy-MM-dd HH:mm:ss"
});
```

### Configuration Loading

```csharp
// Load from JSON file
var config = await ConfigurationLoader.LoadFromFileAsync("transformations.json");

// Load from YAML file  
var config = await ConfigurationLoader.LoadFromFileAsync("transformations.yaml");

// Load from JSON string
var config = ConfigurationLoader.LoadFromJson(jsonString);

// Create pipeline
var pipeline = new TransformationPipeline(config, registry, logger);

// Transform data
var transformedValue = pipeline.TransformCell(
    "events", 
    "timestamp", 
    "1692100856000",
    new CellContext("events", "timestamp", 0, SqliteAffinity.Integer)
);
```

### Custom Transformer Development

```csharp
public class CustomTransformer : CellTransformerBase
{
    public CustomTransformer(IDictionary<string, string> configuration) 
        : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               ctx.Column.Contains("custom_field");
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        
        var prefix = GetConfig("prefix", "TRANSFORMED_");
        var suffix = GetConfig("suffix", "");
        
        return $"{prefix}{raw}{suffix}";
    }
}

// Register custom transformer
builder.Register("custom", config => new CustomTransformer(config));
```

---

## 📈 Performance Characteristics

### Benchmark Results

- **Throughput**: 10,000+ transformations per second
- **Memory**: Constant memory usage with streaming architecture  
- **Concurrency**: Thread-safe design supports parallel processing
- **Batch Processing**: Configurable batch sizes for optimal performance

### Performance Configuration

```json
{
  "global": {
    "performance": {
      "batchSize": 10000,              // Rows per batch
      "enableParallelProcessing": true, // Enable parallel execution
      "maxDegreeOfParallelism": 0      // 0 = auto-detect CPU cores
    }
  }
}
```

### Performance Tips

1. **Use appropriate batch sizes** (10K-50K rows)
2. **Enable parallel processing** for CPU-bound transformations
3. **Pre-compile transformers** using the pipeline compilation
4. **Use specific column targeting** instead of global transformers
5. **Configure appropriate error limits** to avoid excessive logging

---

## 🛡️ Error Handling & Debugging

### Error Tracking

```csharp
// Get error count from pipeline
var errorCount = pipeline.ErrorCount;

// Check if pipeline should stop due to errors
var maxErrors = config.Global.MaxErrors;
if (errorCount >= maxErrors)
{
    // Handle maximum errors exceeded
}
```

### Logging Integration

```csharp
using Microsoft.Extensions.Logging;

// Create logger
var logger = LoggerFactory.Create(builder => 
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug)
).CreateLogger<TransformationPipeline>();

// Create pipeline with logging
var pipeline = new TransformationPipeline(config, registry, logger);
```

### Common Issues & Solutions

#### Transformer Not Applying

```csharp
// Check if transformer is registered
bool isRegistered = registry.IsRegistered("transformer_name");

// Check CanApply logic
var transformer = registry.CreateCell("transformer_name", config);
bool canApply = transformer.CanApply(context);

// Enable force apply
var config = new Dictionary<string, string> { ["forceApply"] = "true" };
```

#### Performance Issues

```csharp
// Increase batch size for large datasets
"batchSize": 50000

// Enable parallel processing
"enableParallelProcessing": true

// Reduce transformer complexity
// Use specific column targeting instead of global transformers
```

#### Memory Usage

```csharp
// Monitor transformer instances
var transformerCount = registry.GetRegisteredNames().Count;

// Use pattern-based targeting instead of creating many transformers
"columnPatterns": ["*_time", "*_date"]

// Clear registry if needed
registry.Clear();
```

---

## 🎯 Best Practices

### Configuration Design

1. **Start with global transformers** for common patterns
2. **Use table-specific transformers** for specialized logic
3. **Leverage pattern matching** to reduce configuration complexity
4. **Set appropriate error limits** based on data quality expectations
5. **Test configurations** with sample data before production use

### Performance Optimization

1. **Pre-compile transformers** during pipeline initialization
2. **Use batch processing** for large datasets
3. **Enable parallel processing** for CPU-bound transformations
4. **Monitor error rates** and adjust error handling strategies
5. **Profile transformer performance** and optimize hot paths

### Security Considerations

1. **Use MaskTransformer** for PII data
2. **Validate configuration files** before loading
3. **Limit regex complexity** to prevent ReDoS attacks
4. **Set timeouts** for complex operations
5. **Audit transformed data** for sensitive information leakage

### Testing Strategy

1. **Unit test custom transformers** with edge cases
2. **Integration test configurations** with real data
3. **Performance test with large datasets** under load
4. **Validate error handling** with malformed data
5. **Test concurrent access** for thread safety

---

## 📝 Example Configurations

### Financial Data Transformation

```json
{
  "tables": {
    "transactions": {
      "columns": {
        "timestamp": [
          {
            "name": "epoch",
            "config": {
              "unit": "ms",
              "format": "yyyy-MM-dd HH:mm:ss",
              "tz": "UTC"
            }
          }
        ],
        "amount": [
          {
            "name": "regex-replace",
            "config": {
              "pattern": "^(\\d+)$",
              "replacement": "$1.00"
            }
          }
        ],
        "card_number": [
          {
            "name": "mask",
            "config": {
              "type": "card"
            }
          }
        ],
        "metadata": [
          {
            "name": "json-pretty",
            "config": {
              "indent": "  "
            }
          }
        ]
      }
    }
  }
}
```

### Log Processing Configuration

```json
{
  "tables": {
    "application_logs": {
      "columns": {
        "timestamp": [
          {
            "name": "date-format",
            "config": {
              "inputFormat": "yyyy-MM-dd HH:mm:ss.fff",
              "outputFormat": "MMM dd HH:mm:ss"
            }
          }
        ],
        "level": [
          {
            "name": "upper",
            "config": {
              "forceApply": "true"
            }
          }
        ],
        "message": [
          {
            "name": "normalize-whitespace"
          },
          {
            "name": "truncate",
            "config": {
              "maxLength": "500",
              "mode": "end"
            }
          }
        ],
        "user_id": [
          {
            "name": "mask",
            "config": {
              "type": "custom",
              "keepStart": "2",
              "keepEnd": "2"
            }
          }
        ]
      }
    }
  }
}
```

### Multi-format JSON Processing

```json
{
  "globalTransformers": [
    {
      "name": "coalesce",
      "config": {
        "default": "[NULL]"
      }
    }
  ],
  "tables": {
    "events": {
      "columns": {
        "payload": [
          {
            "name": "json-validate",
            "conditions": {
              "columnPatterns": ["*_json", "*_data", "payload"]
            }
          },
          {
            "name": "json-compact",
            "conditions": {
              "valuePattern": "^\\s*[{\\[]"
            }
          }
        ],
        "binary_data": [
          {
            "name": "binary-json-decode",
            "config": {
              "encoding": "auto",
              "outputFormat": "pretty"
            }
          }
        ]
      }
    }
  }
}
```

---

## 🔮 Advanced Features

### Conditional Transformations

```json
{
  "name": "mask",
  "conditions": {
    "columnPatterns": ["*email*", "*phone*"],
    "dataTypes": ["text"],
    "valuePattern": ".*@.*"
  },
  "config": {
    "type": "auto"
  }
}
```

### Priority-based Ordering

```json
{
  "columns": {
    "text_field": [
      {
        "name": "trim",
        "priority": 1,
        "config": {}
      },
      {
        "name": "normalize-whitespace", 
        "priority": 2,
        "config": {}
      },
      {
        "name": "truncate",
        "priority": 3,
        "config": {
          "maxLength": "100"
        }
      }
    ]
  }
}
```

### Table Filtering

```json
{
  "tables": {
    "large_table": {
      "filters": {
        "whereClause": "created_at > datetime('now', '-1 month')",
        "maxRows": 100000,
        "includeColumns": ["id", "name", "timestamp", "data"],
        "excludeColumns": ["internal_notes", "debug_info"]
      }
    }
  }
}
```

---

## 📚 Additional Resources

- **[Core Specification](CLAUDE.md)** - Complete DB2XL implementation guide
- **[Getting Started](GETTING_STARTED.md)** - Step-by-step tutorials
- **[API Reference](README.md)** - Complete API documentation
- **[Test Examples](SqliteXport.Tests/Transformers/)** - Comprehensive test suite with examples

---

**Made with ❤️ for intelligent data transformation workflows**