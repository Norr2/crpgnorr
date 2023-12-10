﻿using System.Diagnostics;
using Crpg.Application.Characters.Queries;
using Crpg.Application.Common.Results;
using Crpg.Common.Helpers;
using Crpg.Domain.Entities.Characters;
using Crpg.Domain.Entities.Items;
using Crpg.Domain.Entities.Users;

namespace Crpg.Application.Common.Services;

/// <summary>
/// Common logic for characters.
/// </summary>
internal interface ICharacterService
{
    void SetDefaultValuesForCharacter(Character character);

    /// <summary>
    /// Reset character characteristics.
    /// </summary>
    /// <param name="character">Character to reset.</param>
    /// <param name="respecialization">If the stats points should be redistributed.</param>
    void ResetCharacterCharacteristics(Character character, bool respecialization = false);

    void UpdateRating(Character character, float value, float deviation, float volatility);

    void ResetRating(Character character);

    Error? Retire(Character character);

    void GiveExperience(Character character, int experience, bool useExperienceMultiplier);
}

/// <inheritdoc />
internal class CharacterService : ICharacterService
{
    private readonly IExperienceTable _experienceTable;
    private readonly ICompetitiveRatingModel _competitiveRatingModel;
    private readonly Constants _constants;

    public CharacterService(
        IExperienceTable experienceTable,
        ICompetitiveRatingModel competitiveRatingModel,
        Constants constants)
    {
        _experienceTable = experienceTable;
        _competitiveRatingModel = competitiveRatingModel;
        _constants = constants;
    }

    public void SetDefaultValuesForCharacter(Character character)
    {
        character.Generation = _constants.DefaultGeneration;
        character.Level = _constants.MinimumLevel;
        character.Experience = _experienceTable.GetExperienceForLevel(character.Level);
        character.ForTournament = false;

        ResetCharacterCharacteristics(character);
        ResetRating(character);
    }

    /// <inheritdoc />
    public void ResetCharacterCharacteristics(Character character, bool respecialization = false)
    {
        character.Characteristics = new CharacterCharacteristics
        {
            Attributes = new CharacterAttributes
            {
                Points = _constants.DefaultAttributePoints + (respecialization ? (character.Level - 1) * _constants.AttributePointsPerLevel : 0),
                Strength = _constants.DefaultStrength,
                Agility = _constants.DefaultAgility,
            },
            Skills = new CharacterSkills
            {
                Points = _constants.DefaultSkillPoints + (respecialization ? (character.Level - 1) * _constants.SkillPointsPerLevel : 0),
            },
            WeaponProficiencies = new CharacterWeaponProficiencies
            {
                Points = WeaponProficiencyPointsForLevel(respecialization ? character.Level : 1),
            },
        };
        character.Class = CharacterClass.Peasant;
    }

    public void UpdateRating(Character character, float value, float deviation, float volatility)
    {
        character.Rating = new CharacterRating
        {
            Value = value,
            Deviation = deviation,
            Volatility = volatility,
        };
        character.Rating.CompetitiveValue = _competitiveRatingModel.ComputeCompetitiveRating(character.Rating);
    }

    public void ResetRating(Character character)
    {
        UpdateRating(character, _constants.DefaultRating, _constants.DefaultRatingDeviation,
            _constants.DefaultRatingVolatility);
    }

    public Error? Retire(Character character)
    {
        if (character.Level < _constants.MinimumRetirementLevel)
        {
            return CommonErrors.CharacterLevelRequirementNotMet(_constants.MinimumRetirementLevel, character.Level);
        }

        int heirloomPoints = (int)Math.Pow(2, character.Level - _constants.MinimumRetirementLevel); // to update if level above 31 do not follow the x2 pattern anymore

        character.User!.HeirloomPoints += heirloomPoints;
        character.Generation += 1;
        User user = new();
        int totalGeneration = GetTotalGenerationValue(user);
        character.User.ExperienceMultiplier = totalGeneration * 0.3f + 1f; // To update with proper equation
        character.Level = _constants.MinimumLevel;
        character.Experience = 0;
        character.EquippedItems.Clear();
        ResetCharacterCharacteristics(character, respecialization: false);
        return null;
    }

    public int GetTotalGenerationValue(User user)
    {
        int totalGeneration = 0;

        if (user.Characters != null)
        {
            foreach (var character in user.Characters)
            {
                totalGeneration += character.Generation;
            }
        }

        user.GenerationUser = totalGeneration;
        return totalGeneration;
    }

    public void GiveExperience(Character character, int experience, bool useExperienceMultiplier)
    {
        Debug.Assert(experience >= 0, "Given experience should be positive");

        if (character.ForTournament)
        {
            return;
        }

        character.Experience += useExperienceMultiplier
            ? (int)(character.User!.ExperienceMultiplier * experience)
            : experience;
        int newLevel = _experienceTable.GetLevelForExperience(character.Experience);
        int levelDiff = newLevel - character.Level;
        if (levelDiff != 0) // if character leveled up
        {
            character.Characteristics.Attributes.Points += levelDiff * _constants.AttributePointsPerLevel;
            character.Characteristics.Skills.Points += levelDiff * _constants.SkillPointsPerLevel;
            character.Characteristics.WeaponProficiencies.Points += WeaponProficiencyPointsForLevel(newLevel) - WeaponProficiencyPointsForLevel(character.Level);
            character.Level = newLevel;
        }
    }

    private int WeaponProficiencyPointsForLevel(int lvl) =>
        (int)MathHelper.ApplyPolynomialFunction(lvl, _constants.WeaponProficiencyPointsForLevelCoefs);
}
