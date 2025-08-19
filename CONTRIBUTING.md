# Contributing to DB2XL

Thank you for your interest in contributing to DB2XL! This document provides guidelines for contributing to this proprietary project.

## 🔒 Important Notice

This is a **proprietary software project**. All contributions are subject to the project's proprietary license terms. By contributing, you agree that your contributions will be licensed under the same proprietary terms.

## 🚀 Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Visual Studio 2022, VS Code, or JetBrains Rider
- Git for version control

### Development Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/revred/DB2XL.git
   cd DB2XL
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the solution**
   ```bash
   dotnet build
   ```

4. **Run tests**
   ```bash
   dotnet test
   ```

## 🏗️ Project Structure

```
DB2XL/
├── SqliteXport/              # Core library
├── SqliteXport.Tests/        # Test suite  
├── CLAUDE.md                 # Complete specification
├── README.md                 # Project documentation
└── DB2XL.sln                 # Solution file
```

## 📋 Development Guidelines

### Code Standards

- **Target Framework**: .NET 8.0
- **Nullable Reference Types**: Enabled
- **Code Style**: Follow existing patterns and conventions
- **Documentation**: XML documentation for public APIs
- **Testing**: Comprehensive test coverage required

### Key Principles

1. **Deterministic Output**: Ensure bit-for-bit consistency
2. **Data Fidelity**: Preserve exact data representation
3. **Robustness**: Handle edge cases gracefully
4. **Performance**: Efficient memory usage and processing
5. **Compatibility**: Support wide range of SQLite schemas

### Testing Requirements

- **Unit Tests**: Core component functionality
- **Integration Tests**: End-to-end export scenarios
- **Performance Tests**: Large dataset handling
- **Edge Case Tests**: Unicode, special characters, limits
- **Validation Tests**: Data integrity verification

Example test pattern:
```csharp
[Theory]
[InlineData("TestCase1", parameter1, "Description")]
[InlineData("TestCase2", parameter2, "Description")]
public void Export_Scenario_ShouldHandleCorrectly(string name, int param, string desc)
{
    // Arrange
    var options = new SqliteToExcelOptions { /* config */ };
    
    // Act
    SqliteToExcel.Export(dbPath, xlsxPath, options);
    
    // Assert
    var validation = ExportValidator.ValidateExport(dbPath, xlsxPath);
    Assert.True(validation.IsValid);
}
```

### Commit Guidelines

- **Conventional Commits**: Use conventional commit format
- **Clear Messages**: Descriptive commit messages
- **Atomic Changes**: One logical change per commit
- **Tests Included**: All commits should include relevant tests

Example commit messages:
```
feat: add support for custom BLOB rendering modes
fix: handle empty tables with proper header generation  
test: add performance validation for large datasets
docs: update README with new configuration options
refactor: simplify sheet name sanitization logic
```

## 🐛 Issue Reporting

### Bug Reports

Please include:
- **Clear description** of the issue
- **Steps to reproduce** the problem
- **Expected vs actual behavior**
- **Environment details** (OS, .NET version)
- **Sample database** (if possible and not sensitive)
- **Error messages** and stack traces

### Feature Requests

Please include:
- **Use case description** and business justification
- **Proposed solution** or implementation approach
- **Backwards compatibility** considerations
- **Testing strategy** for the new feature

## 🔍 Code Review Process

1. **Self Review**: Review your own changes thoroughly
2. **Testing**: Ensure all tests pass
3. **Documentation**: Update relevant documentation
4. **Pull Request**: Create PR with clear description
5. **Review**: Address feedback from maintainers
6. **Merge**: Changes merged after approval

### Pull Request Checklist

- [ ] Code follows project conventions
- [ ] All tests pass (`dotnet test`)
- [ ] New functionality includes tests
- [ ] Documentation updated (if applicable)
- [ ] No breaking changes (or properly documented)
- [ ] Performance impact considered
- [ ] Security implications reviewed

## 🎯 Areas for Contribution

### High Priority
- **Performance optimization** for very large datasets
- **Additional BLOB rendering modes** (custom formats)
- **Enhanced metadata tracking** (more database info)
- **Streaming export variant** using OpenXML SAX
- **Error handling improvements** with better diagnostics

### Medium Priority  
- **Additional export formats** (CSV, JSON variants)
- **Configuration validation** and helpful error messages
- **Progress reporting** for long-running exports
- **Memory usage optimization** for wide tables
- **Unit test coverage expansion**

### Low Priority
- **Documentation improvements** and examples
- **Code style and refactoring** for maintainability
- **Development tooling** and automation
- **Example applications** and demos

## 📞 Communication

- **Questions**: Create GitHub discussions for general questions
- **Issues**: Use GitHub issues for bugs and feature requests  
- **Security**: Contact maintainers directly for security issues
- **Licensing**: Contact copyright holder for licensing questions

## 🙏 Recognition

Contributors will be recognized in:
- Project documentation
- Release notes  
- GitHub contributor listings
- Special thanks in major releases

## 📝 Legal

By contributing to this project, you agree that:
- Your contributions are your original work
- You have the right to submit the contributions
- Your contributions will be subject to the project's proprietary license
- You understand this is proprietary software with usage restrictions

---

Thank you for helping make DB2XL better! 🚀