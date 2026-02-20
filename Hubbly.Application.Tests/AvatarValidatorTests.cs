using Hubbly.Application.Services;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Hubbly.Application.Tests;

public class AvatarValidatorTests
{
    private readonly IAvatarValidator _validator;

    public AvatarValidatorTests()
    {
        var loggerMock = new Mock<ILogger<AvatarValidator>>();
        _validator = new AvatarValidator(loggerMock.Object);
    }

    [Fact]
    public void IsValidConfig_WithValidMaleConfig_ReturnsTrue()
    {
        // Arrange
        var validConfig = new AvatarConfig
        {
            Gender = "male",
            BaseModelId = "male_base",
            Pose = "standing",
            Components = new Dictionary<string, string>
            {
                { "top", "male_tshirt" },
                { "bottom", "male_jeans" },
                { "hair", "male_short" },
                { "shoes", "male_sneakers" }
            }
        };
        var ownedAssets = new List<string>
        {
            "male_tshirt",
            "male_jeans",
            "male_short",
            "male_sneakers"
        };

        // Act
        var result = _validator.IsValidConfig(validConfig.ToJson(), ownedAssets);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidConfig_WithValidFemaleConfig_ReturnsTrue()
    {
        // Arrange
        var validConfig = new AvatarConfig
        {
            Gender = "female",
            BaseModelId = "female_base",
            Pose = "standing",
            Components = new Dictionary<string, string>
            {
                { "top", "female_blouse" },
                { "bottom", "female_skirt" },
                { "hair", "female_bob" },
                { "shoes", "female_sandals" }
            }
        };
        var ownedAssets = new List<string>
        {
            "female_blouse",
            "female_skirt",
            "female_bob",
            "female_sandals"
        };

        // Act
        var result = _validator.IsValidConfig(validConfig.ToJson(), ownedAssets);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidConfig_WithMissingAsset_ReturnsTrue_ForNow()
    {
        // Arrange
        var config = new AvatarConfig
        {
            Gender = "male",
            BaseModelId = "male_base",
            Pose = "standing",
            Components = new Dictionary<string, string>
            {
                { "top", "male_tshirt" },
                { "bottom", "male_jeans" },
                { "hair", "male_short" },
                { "shoes", "male_sneakers" }
            }
        };
        var ownedAssets = new List<string>
        {
            "male_tshirt",
            "male_jeans",
            "male_short"
            // Missing male_sneakers - but asset validation not implemented yet
        };

        // Act
        var result = _validator.IsValidConfig(config.ToJson(), ownedAssets);

        // Assert
        // Currently returns true because asset validation is not implemented (TODO in code)
        Assert.True(result);
    }

    [Fact]
    public void IsValidConfig_WithInvalidJson_ReturnsFalse()
    {
        // Arrange
        var invalidJson = "invalid json string";
        var ownedAssets = new List<string>();

        // Act
        var result = _validator.IsValidConfig(invalidJson, ownedAssets);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidConfig_WithNullConfig_ReturnsFalse()
    {
        // Act
        var result = _validator.IsValidConfig(null!, new List<string>());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidConfig_WithEmptyConfig_ReturnsFalse()
    {
        // Act
        var result = _validator.IsValidConfig("", new List<string>());

        // Assert
        Assert.False(result);
    }
}