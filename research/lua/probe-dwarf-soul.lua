local json = require('json')

local function safe(fn)
    local ok, value = pcall(fn)
    if ok then
        return value, nil
    end

    return nil, value
end

local function add_error(errors, field, err)
    if err ~= nil then
        errors[field] = tostring(err)
    end
end

local function json_scalar(value)
    local value_type = type(value)

    if value == nil then
        return nil
    end

    if value_type == "string" or value_type == "number" or value_type == "boolean" then
        return value
    end

    return tostring(value)
end

local function enum_name(enum_table, value)
    if enum_table == nil or value == nil then
        return nil
    end

    local result, err = safe(function()
        return enum_table[value]
    end)

    if err ~= nil then
        return nil
    end

    return json_scalar(result)
end

local function maybe_errors(errors)
    if next(errors) then
        return errors
    end

    return nil
end

local function abs_number(value)
    if value == nil then
        return nil
    end

    if value < 0 then
        return -value
    end

    return value
end

local function shallow_copy_array(items, limit)
    local result = {}

    if items == nil then
        return result
    end

    for index, item in ipairs(items) do
        if limit ~= nil and index > limit then
            break
        end

        table.insert(result, item)
    end

    return result
end

local function find_citizen_by_id(unit_id)
    local citizens = dfhack.units.getCitizens()

    for _, unit in ipairs(citizens) do
        if tostring(unit.id) == tostring(unit_id) then
            return unit
        end
    end

    return nil
end

local function summarize_identity(unit)
    local errors = {}

    local readable_name, readable_name_err = safe(function()
        return dfhack.units.getReadableName(unit)
    end)

    local profession_name, profession_name_err = safe(function()
        return dfhack.units.getProfessionName(unit)
    end)

    local profession_id, profession_id_err = safe(function()
        return dfhack.units.getProfession(unit)
    end)

    add_error(errors, "readableName", readable_name_err)
    add_error(errors, "professionName", profession_name_err)
    add_error(errors, "professionId", profession_id_err)

    return {
        id = tostring(unit.id),
        rawId = json_scalar(unit.id),
        readableName = json_scalar(readable_name),
        professionName = json_scalar(profession_name),
        professionId = json_scalar(profession_id),
        professionToken = enum_name(df.profession, profession_id),
        errors = maybe_errors(errors)
    }
end

local function summarize_flags(unit)
    local errors = {}

    local is_citizen, is_citizen_err = safe(function()
        return dfhack.units.isCitizen(unit, true)
    end)

    local is_resident, is_resident_err = safe(function()
        return dfhack.units.isResident(unit, true)
    end)

    local is_sane, is_sane_err = safe(function()
        return dfhack.units.isSane(unit)
    end)

    local is_alive, is_alive_err = safe(function()
        return dfhack.units.isAlive(unit)
    end)

    local is_dwarf, is_dwarf_err = safe(function()
        return dfhack.units.isDwarf(unit)
    end)

    local is_active, is_active_err = safe(function()
        return dfhack.units.isActive(unit)
    end)

    add_error(errors, "isCitizen", is_citizen_err)
    add_error(errors, "isResident", is_resident_err)
    add_error(errors, "isSane", is_sane_err)
    add_error(errors, "isAlive", is_alive_err)
    add_error(errors, "isDwarf", is_dwarf_err)
    add_error(errors, "isActive", is_active_err)

    return {
        isCitizen = json_scalar(is_citizen),
        isResident = json_scalar(is_resident),
        isSane = json_scalar(is_sane),
        isAlive = json_scalar(is_alive),
        isDwarf = json_scalar(is_dwarf),
        isActive = json_scalar(is_active),
        errors = maybe_errors(errors)
    }
end

local function summarize_work(unit)
    local errors = {}

    local current_job_type, current_job_err = safe(function()
        if unit.job and unit.job.current_job then
            return enum_name(df.job_type, unit.job.current_job.job_type)
        end

        return nil
    end)

    add_error(errors, "currentJobType", current_job_err)

    return {
        currentJobType = json_scalar(current_job_type),
        errors = maybe_errors(errors)
    }
end

local function summarize_stress(unit)
    local errors = {}

    local raw_stress, raw_stress_err = safe(function()
        if unit.status and unit.status.current_soul and unit.status.current_soul.personality then
            return unit.status.current_soul.personality.stress
        end

        return nil
    end)

    local longterm_stress, longterm_stress_err = safe(function()
        if unit.status and unit.status.current_soul and unit.status.current_soul.personality then
            return unit.status.current_soul.personality.longterm_stress
        end

        return nil
    end)

    local category, category_err = safe(function()
        return dfhack.units.getStressCategory(unit)
    end)

    add_error(errors, "rawStress", raw_stress_err)
    add_error(errors, "longtermStress", longterm_stress_err)
    add_error(errors, "stressCategory", category_err)

    return {
        raw = json_scalar(raw_stress),
        longterm = json_scalar(longterm_stress),
        category = json_scalar(category),
        categoryScale = "0-most-stressed-6-least-stressed",
        errors = maybe_errors(errors)
    }
end

local function summarize_skills(unit, soul)
    local errors = {}
    local skills = {}

    local skill_list, skill_list_err = safe(function()
        return soul.skills
    end)

    add_error(errors, "skills", skill_list_err)

    if skill_list ~= nil then
        for _, skill in ipairs(skill_list) do
            local skill_id, skill_id_err = safe(function()
                return skill.id
            end)

            local rating, rating_err = safe(function()
                return skill.rating
            end)

            local rust, rust_err = safe(function()
                return skill.rusty
            end)

            local nominal, nominal_err = safe(function()
                return dfhack.units.getNominalSkill(unit, skill_id)
            end)

            local effective, effective_err = safe(function()
                return dfhack.units.getEffectiveSkill(unit, skill_id)
            end)

            local experience, experience_err = safe(function()
                return dfhack.units.getExperience(unit, skill_id)
            end)

            local total_experience, total_experience_err = safe(function()
                return dfhack.units.getExperience(unit, skill_id, true)
            end)

            local item_errors = {}

            add_error(item_errors, "id", skill_id_err)
            add_error(item_errors, "rating", rating_err)
            add_error(item_errors, "rust", rust_err)
            add_error(item_errors, "nominal", nominal_err)
            add_error(item_errors, "effective", effective_err)
            add_error(item_errors, "experience", experience_err)
            add_error(item_errors, "totalExperience", total_experience_err)

            table.insert(skills, {
                id = json_scalar(skill_id),
                token = enum_name(df.job_skill, skill_id),
                rating = json_scalar(rating),
                nominal = json_scalar(nominal),
                effective = json_scalar(effective),
                experience = json_scalar(experience),
                totalExperience = json_scalar(total_experience),
                rust = json_scalar(rust),
                errors = maybe_errors(item_errors)
            })
        end
    end

    return {
        count = #skills,
        items = skills,
        errors = maybe_errors(errors)
    }
end

local function summarize_personality_state(personality)
    local errors = {}

    local stress, stress_err = safe(function()
        return personality.stress
    end)

    local longterm_stress, longterm_stress_err = safe(function()
        return personality.longterm_stress
    end)

    local time_without_distress, time_without_distress_err = safe(function()
        return personality.time_without_distress
    end)

    local time_without_eustress, time_without_eustress_err = safe(function()
        return personality.time_without_eustress
    end)

    local likes_outdoors, likes_outdoors_err = safe(function()
        return personality.likes_outdoors
    end)

    local combat_hardened, combat_hardened_err = safe(function()
        return personality.combat_hardened
    end)

    local outdoor_dislike_counter, outdoor_dislike_counter_err = safe(function()
        return personality.outdoor_dislike_counter
    end)

    local current_focus, current_focus_err = safe(function()
        return personality.current_focus
    end)

    local undistracted_focus, undistracted_focus_err = safe(function()
        return personality.undistracted_focus
    end)

    local temptation_greed, temptation_greed_err = safe(function()
        return personality.temptation_greed
    end)

    local temptation_lust, temptation_lust_err = safe(function()
        return personality.temptation_lust
    end)

    local temptation_power, temptation_power_err = safe(function()
        return personality.temptation_power
    end)

    local temptation_anger, temptation_anger_err = safe(function()
        return personality.temptation_anger
    end)

    local flag_has_unmet_needs, flag_has_unmet_needs_err = safe(function()
        if personality.flags then
            return personality.flags.has_unmet_needs
        end

        return nil
    end)

    local flag_distraction_calculated, flag_distraction_calculated_err = safe(function()
        if personality.flags then
            return personality.flags.distraction_calculated
        end

        return nil
    end)

    add_error(errors, "stress", stress_err)
    add_error(errors, "longtermStress", longterm_stress_err)
    add_error(errors, "timeWithoutDistress", time_without_distress_err)
    add_error(errors, "timeWithoutEustress", time_without_eustress_err)
    add_error(errors, "likesOutdoors", likes_outdoors_err)
    add_error(errors, "combatHardened", combat_hardened_err)
    add_error(errors, "outdoorDislikeCounter", outdoor_dislike_counter_err)
    add_error(errors, "currentFocus", current_focus_err)
    add_error(errors, "undistractedFocus", undistracted_focus_err)
    add_error(errors, "temptationGreed", temptation_greed_err)
    add_error(errors, "temptationLust", temptation_lust_err)
    add_error(errors, "temptationPower", temptation_power_err)
    add_error(errors, "temptationAnger", temptation_anger_err)
    add_error(errors, "flags.hasUnmetNeeds", flag_has_unmet_needs_err)
    add_error(errors, "flags.distractionCalculated", flag_distraction_calculated_err)

    return {
        stress = json_scalar(stress),
        longtermStress = json_scalar(longterm_stress),
        timeWithoutDistress = json_scalar(time_without_distress),
        timeWithoutEustress = json_scalar(time_without_eustress),
        likesOutdoors = json_scalar(likes_outdoors),
        combatHardened = json_scalar(combat_hardened),
        outdoorDislikeCounter = json_scalar(outdoor_dislike_counter),
        currentFocus = json_scalar(current_focus),
        undistractedFocus = json_scalar(undistracted_focus),
        temptation = {
            greed = json_scalar(temptation_greed),
            lust = json_scalar(temptation_lust),
            power = json_scalar(temptation_power),
            anger = json_scalar(temptation_anger)
        },
        flags = {
            hasUnmetNeeds = json_scalar(flag_has_unmet_needs),
            distractionCalculated = json_scalar(flag_distraction_calculated)
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_traits(personality)
    local errors = {}
    local traits = {}

    local trait_list, trait_list_err = safe(function()
        return personality.traits
    end)

    add_error(errors, "traits", trait_list_err)

    if trait_list ~= nil then
        for index, trait_value in ipairs(trait_list) do
            local raw_value = json_scalar(trait_value)
            local deviation = nil

            if raw_value ~= nil then
                deviation = raw_value - 50
            end

            table.insert(traits, {
                index = json_scalar(index),
                token = enum_name(df.personality_facet_type, index),
                value = raw_value,
                deviationFromNeutral50 = json_scalar(deviation),
                absDeviationFromNeutral50 = json_scalar(abs_number(deviation))
            })
        end
    end

    return {
        count = #traits,
        items = traits,
        notes = {
            indexSource = "unit_personality.traits static-array indexed by personality_facet_type",
            neutralApproximation = 50
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_values(personality)
    local errors = {}
    local values = {}

    local value_list, value_list_err = safe(function()
        return personality.values
    end)

    add_error(errors, "values", value_list_err)

    if value_list ~= nil then
        for index, belief in ipairs(value_list) do
            local belief_type, belief_type_err = safe(function()
                return belief.type
            end)

            local belief_strength, belief_strength_err = safe(function()
                return belief.strength
            end)

            local item_errors = {}

            add_error(item_errors, "type", belief_type_err)
            add_error(item_errors, "strength", belief_strength_err)

            table.insert(values, {
                index = json_scalar(index),
                type = json_scalar(belief_type),
                token = enum_name(df.value_type, belief_type),
                strength = json_scalar(belief_strength),
                errors = maybe_errors(item_errors)
            })
        end
    end

    return {
        count = #values,
        items = values,
        notes = {
            sourceStruct = "personality_valuest",
            fields = "type, strength",
            intentionallyNotRead = "value"
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_needs(personality)
    local errors = {}
    local needs = {}

    local need_list, need_list_err = safe(function()
        return personality.needs
    end)

    add_error(errors, "needs", need_list_err)

    if need_list ~= nil then
        for index, need in ipairs(need_list) do
            local need_id, need_id_err = safe(function()
                return need.id
            end)

            local focus_level, focus_level_err = safe(function()
                return need.focus_level
            end)

            local need_level, need_level_err = safe(function()
                return need.need_level
            end)

            local deity_id, deity_id_err = safe(function()
                return need.deity_id
            end)

            local is_unmet = nil
            local is_deeply_unmet = nil

            if focus_level ~= nil then
                is_unmet = focus_level < 0
                is_deeply_unmet = focus_level < -999
            end

            local item_errors = {}

            add_error(item_errors, "id", need_id_err)
            add_error(item_errors, "focusLevel", focus_level_err)
            add_error(item_errors, "needLevel", need_level_err)
            add_error(item_errors, "deityId", deity_id_err)

            table.insert(needs, {
                index = json_scalar(index),
                id = json_scalar(need_id),
                token = enum_name(df.need_type, need_id),
                focusLevel = json_scalar(focus_level),
                needLevel = json_scalar(need_level),
                deityId = json_scalar(deity_id),
                isUnmet = json_scalar(is_unmet),
                isDeeplyUnmet = json_scalar(is_deeply_unmet),
                errors = maybe_errors(item_errors)
            })
        end
    end

    return {
        count = #needs,
        items = needs,
        notes = {
            focusLevel = "goes toward 400 when satisfied; below 0 means unmet pressure",
            deeplyUnmetThreshold = "focusLevel below -999 is represented by personality flag has_unmet_needs",
            needLevel = "raw severity/decay pressure, not the current satisfaction level"
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_mannerisms(personality)
    local errors = {}
    local mannerisms = {}

    local mannerism_list, mannerism_list_err = safe(function()
        return personality.mannerism
    end)

    add_error(errors, "mannerism", mannerism_list_err)

    if mannerism_list ~= nil then
        for index, mannerism in ipairs(mannerism_list) do
            local mannerism_type, mannerism_type_err = safe(function()
                return mannerism.type
            end)

            local situation_type, situation_type_err = safe(function()
                return mannerism.situation
            end)

            local item_errors = {}

            add_error(item_errors, "type", mannerism_type_err)
            add_error(item_errors, "situation", situation_type_err)

            table.insert(mannerisms, {
                index = json_scalar(index),
                type = json_scalar(mannerism_type),
                token = enum_name(df.mannerism_type, mannerism_type),
                situation = json_scalar(situation_type),
                situationToken = enum_name(df.mannerism_situation_type, situation_type),
                errors = maybe_errors(item_errors)
            })
        end
    end

    return {
        count = #mannerisms,
        items = mannerisms,
        errors = maybe_errors(errors)
    }
end

local function summarize_habits(personality)
    local errors = {}
    local habits = {}

    local habit_list, habit_list_err = safe(function()
        return personality.habit
    end)

    add_error(errors, "habit", habit_list_err)

    if habit_list ~= nil then
        for index, habit_id in ipairs(habit_list) do
            table.insert(habits, {
                index = json_scalar(index),
                id = json_scalar(habit_id),
                token = enum_name(df.habit_type, habit_id)
            })
        end
    end

    return {
        count = #habits,
        items = habits,
        errors = maybe_errors(errors)
    }
end

local function summarize_preferences(personality)
    local errors = {}
    local preferences = {}

    local preference_list, preference_list_err = safe(function()
        return personality.preferences
    end)

    add_error(errors, "preferences", preference_list_err)

    if preference_list ~= nil then
        for index, preference in ipairs(preference_list) do
            local preference_type, preference_type_err = safe(function()
                return preference.type
            end)

            local var1, var1_err = safe(function()
                return preference.var1
            end)

            local var2, var2_err = safe(function()
                return preference.var2
            end)

            local var3, var3_err = safe(function()
                return preference.var3
            end)

            local var4, var4_err = safe(function()
                return preference.var4
            end)

            local seed, seed_err = safe(function()
                return preference.seed
            end)

            local item_errors = {}

            add_error(item_errors, "type", preference_type_err)
            add_error(item_errors, "var1", var1_err)
            add_error(item_errors, "var2", var2_err)
            add_error(item_errors, "var3", var3_err)
            add_error(item_errors, "var4", var4_err)
            add_error(item_errors, "seed", seed_err)

            table.insert(preferences, {
                index = json_scalar(index),
                type = json_scalar(preference_type),
                token = enum_name(df.personality_preference_type, preference_type),
                var1 = json_scalar(var1),
                var2 = json_scalar(var2),
                var3 = json_scalar(var3),
                var4 = json_scalar(var4),
                seed = json_scalar(seed),
                errors = maybe_errors(item_errors)
            })
        end
    end

    return {
        count = #preferences,
        items = preferences,
        notes = {
            warning = "Preference var fields are raw ids and are not resolved to material/creature/item names in this probe."
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_emotion_item(emotion, index)
    local emotion_type, emotion_type_err = safe(function()
        return emotion.type
    end)

    local strength, strength_err = safe(function()
        return emotion.strength
    end)

    local relative_strength, relative_strength_err = safe(function()
        return emotion.relative_strength
    end)

    local thought, thought_err = safe(function()
        return emotion.thought
    end)

    local subthought, subthought_err = safe(function()
        return emotion.subthought
    end)

    local severity, severity_err = safe(function()
        return emotion.severity
    end)

    local year, year_err = safe(function()
        return emotion.year
    end)

    local year_tick, year_tick_err = safe(function()
        return emotion.year_tick
    end)

    local item_errors = {}

    add_error(item_errors, "type", emotion_type_err)
    add_error(item_errors, "strength", strength_err)
    add_error(item_errors, "relativeStrength", relative_strength_err)
    add_error(item_errors, "thought", thought_err)
    add_error(item_errors, "subthought", subthought_err)
    add_error(item_errors, "severity", severity_err)
    add_error(item_errors, "year", year_err)
    add_error(item_errors, "yearTick", year_tick_err)

    return {
        index = json_scalar(index),
        type = json_scalar(emotion_type),
        token = enum_name(df.emotion_type, emotion_type),
        strength = json_scalar(strength),
        relativeStrength = json_scalar(relative_strength),
        thought = json_scalar(thought),
        thoughtToken = enum_name(df.unit_thought_type, thought),
        subthought = json_scalar(subthought),
        severity = json_scalar(severity),
        year = json_scalar(year),
        yearTick = json_scalar(year_tick),
        errors = maybe_errors(item_errors)
    }
end

local function summarize_emotions(personality)
    local errors = {}
    local emotions = {}

    local emotion_list, emotion_list_err = safe(function()
        return personality.emotions
    end)

    add_error(errors, "emotions", emotion_list_err)

    if emotion_list ~= nil then
        for index, emotion in ipairs(emotion_list) do
            table.insert(emotions, summarize_emotion_item(emotion, index))
        end
    end

    return {
        count = #emotions,
        items = emotions,
        notes = {
            sourceStruct = "personality_moodst",
            warning = "Raw emotional moods, not yet interpreted as prose."
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_memory_item(memory, index)
    local memory_type, memory_type_err = safe(function()
        return memory.type
    end)

    local strength, strength_err = safe(function()
        return memory.strength
    end)

    local relative_strength, relative_strength_err = safe(function()
        return memory.relative_strength
    end)

    local thought, thought_err = safe(function()
        return memory.thought
    end)

    local subthought, subthought_err = safe(function()
        return memory.subthought
    end)

    local severity, severity_err = safe(function()
        return memory.severity
    end)

    local year, year_err = safe(function()
        return memory.year
    end)

    local year_tick, year_tick_err = safe(function()
        return memory.year_tick
    end)

    local created_year, created_year_err = safe(function()
        return memory.created_year
    end)

    local created_tick, created_tick_err = safe(function()
        return memory.created_tick
    end)

    local has_remembered, has_remembered_err = safe(function()
        if memory.flags then
            return memory.flags.has_remembered
        end

        return nil
    end)

    local item_errors = {}

    add_error(item_errors, "type", memory_type_err)
    add_error(item_errors, "strength", strength_err)
    add_error(item_errors, "relativeStrength", relative_strength_err)
    add_error(item_errors, "thought", thought_err)
    add_error(item_errors, "subthought", subthought_err)
    add_error(item_errors, "severity", severity_err)
    add_error(item_errors, "year", year_err)
    add_error(item_errors, "yearTick", year_tick_err)
    add_error(item_errors, "createdYear", created_year_err)
    add_error(item_errors, "createdTick", created_tick_err)
    add_error(item_errors, "flags.hasRemembered", has_remembered_err)

    return {
        index = json_scalar(index),
        type = json_scalar(memory_type),
        token = enum_name(df.emotion_type, memory_type),
        strength = json_scalar(strength),
        relativeStrength = json_scalar(relative_strength),
        thought = json_scalar(thought),
        thoughtToken = enum_name(df.unit_thought_type, thought),
        subthought = json_scalar(subthought),
        severity = json_scalar(severity),
        year = json_scalar(year),
        yearTick = json_scalar(year_tick),
        createdYear = json_scalar(created_year),
        createdTick = json_scalar(created_tick),
        flags = {
            hasRemembered = json_scalar(has_remembered)
        },
        errors = maybe_errors(item_errors)
    }
end

local function summarize_memory_array(memory_array)
    local items = {}

    if memory_array ~= nil then
        for index, memory in ipairs(memory_array) do
            table.insert(items, summarize_memory_item(memory, index))
        end
    end

    return items
end

local function summarize_memories(personality)
    local errors = {}

    local memory_handler, memory_handler_err = safe(function()
        return personality.memories
    end)

    add_error(errors, "memories", memory_handler_err)

    if memory_handler == nil then
        return {
            present = false,
            shortterm = {
                count = 0,
                items = {}
            },
            longterm = {
                count = 0,
                items = {}
            },
            core = {
                count = 0,
                items = {}
            },
            errors = maybe_errors(errors)
        }
    end

    local shortterm_raw, shortterm_err = safe(function()
        return memory_handler.shortterm
    end)

    local longterm_raw, longterm_err = safe(function()
        return memory_handler.longterm
    end)

    local core_raw, core_err = safe(function()
        return memory_handler.core_memories
    end)

    add_error(errors, "shortterm", shortterm_err)
    add_error(errors, "longterm", longterm_err)
    add_error(errors, "coreMemories", core_err)

    local shortterm_items = summarize_memory_array(shortterm_raw)
    local longterm_items = summarize_memory_array(longterm_raw)
    local core_items = {}

    if core_raw ~= nil then
        for index, core_memory in ipairs(core_raw) do
            local memory, memory_err = safe(function()
                return core_memory.memory
            end)

            local changed_facet, changed_facet_err = safe(function()
                return core_memory.changed_facet
            end)

            local facet_old, facet_old_err = safe(function()
                return core_memory.facet_old
            end)

            local facet_new, facet_new_err = safe(function()
                return core_memory.facet_new
            end)

            local changed_value, changed_value_err = safe(function()
                return core_memory.changed_value
            end)

            local value_old, value_old_err = safe(function()
                return core_memory.value_old
            end)

            local value_new, value_new_err = safe(function()
                return core_memory.value_new
            end)

            local item_errors = {}

            add_error(item_errors, "memory", memory_err)
            add_error(item_errors, "changedFacet", changed_facet_err)
            add_error(item_errors, "facetOld", facet_old_err)
            add_error(item_errors, "facetNew", facet_new_err)
            add_error(item_errors, "changedValue", changed_value_err)
            add_error(item_errors, "valueOld", value_old_err)
            add_error(item_errors, "valueNew", value_new_err)

            table.insert(core_items, {
                index = json_scalar(index),
                memory = memory ~= nil and summarize_memory_item(memory, index) or nil,
                changedFacet = json_scalar(changed_facet),
                changedFacetToken = enum_name(df.personality_facet_type, changed_facet),
                facetOld = json_scalar(facet_old),
                facetNew = json_scalar(facet_new),
                changedValue = json_scalar(changed_value),
                changedValueToken = enum_name(df.value_type, changed_value),
                valueOld = json_scalar(value_old),
                valueNew = json_scalar(value_new),
                errors = maybe_errors(item_errors)
            })
        end
    end

    return {
        present = true,
        shortterm = {
            count = #shortterm_items,
            items = shortterm_items
        },
        longterm = {
            count = #longterm_items,
            items = longterm_items
        },
        core = {
            count = #core_items,
            items = core_items
        },
        notes = {
            warning = "Raw memory fields. Some empty/default slots may appear."
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_personality(unit, soul)
    local errors = {}

    local personality, personality_err = safe(function()
        return soul.personality
    end)

    add_error(errors, "personality", personality_err)

    if personality == nil then
        return {
            present = false,
            state = nil,
            traits = {
                count = 0,
                items = {}
            },
            values = {
                count = 0,
                items = {}
            },
            needs = {
                count = 0,
                items = {}
            },
            mannerisms = {
                count = 0,
                items = {}
            },
            habits = {
                count = 0,
                items = {}
            },
            preferences = {
                count = 0,
                items = {}
            },
            emotions = {
                count = 0,
                items = {}
            },
            memories = {
                present = false
            },
            errors = maybe_errors(errors)
        }
    end

    return {
        present = true,
        state = summarize_personality_state(personality),
        traits = summarize_traits(personality),
        values = summarize_values(personality),
        needs = summarize_needs(personality),
        mannerisms = summarize_mannerisms(personality),
        habits = summarize_habits(personality),
        preferences = summarize_preferences(personality),
        emotions = summarize_emotions(personality),
        memories = summarize_memories(personality),
        errors = maybe_errors(errors)
    }
end

local function build_top_skills(skills_result, limit)
    local items = shallow_copy_array(skills_result.items)

    table.sort(items, function(a, b)
        local a_rating = a.effective or a.rating or 0
        local b_rating = b.effective or b.rating or 0

        if a_rating ~= b_rating then
            return a_rating > b_rating
        end

        return (a.totalExperience or 0) > (b.totalExperience or 0)
    end)

    local result = {}

    for _, skill in ipairs(items) do
        if (skill.effective or skill.rating or 0) > 0 then
            table.insert(result, skill)
        end

        if #result >= limit then
            break
        end
    end

    return result
end

local function build_extreme_traits(traits_result, limit)
    local items = {}

    for _, trait in ipairs(traits_result.items or {}) do
        if trait.value ~= nil and trait.absDeviationFromNeutral50 ~= nil and trait.absDeviationFromNeutral50 >= 10 then
            table.insert(items, {
                token = trait.token,
                value = trait.value,
                deviationFromNeutral50 = trait.deviationFromNeutral50,
                absDeviationFromNeutral50 = trait.absDeviationFromNeutral50,
                polarity = trait.deviationFromNeutral50 >= 0 and "high" or "low"
            })
        end
    end

    table.sort(items, function(a, b)
        if a.absDeviationFromNeutral50 ~= b.absDeviationFromNeutral50 then
            return a.absDeviationFromNeutral50 > b.absDeviationFromNeutral50
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return shallow_copy_array(items, limit)
end

local function build_strong_values(values_result, limit)
    local items = {}

    for _, value in ipairs(values_result.items or {}) do
        if value.strength ~= nil then
            table.insert(items, {
                token = value.token,
                type = value.type,
                strength = value.strength,
                absStrength = abs_number(value.strength),
                polarity = value.strength >= 0 and "positive" or "negative"
            })
        end
    end

    table.sort(items, function(a, b)
        if a.absStrength ~= b.absStrength then
            return a.absStrength > b.absStrength
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return shallow_copy_array(items, limit)
end

local function build_strong_needs(needs_result, limit)
    local items = {}

    for _, need in ipairs(needs_result.items or {}) do
        local include = false

        if need.needLevel ~= nil and need.needLevel >= 2 then
            include = true
        end

        if need.focusLevel ~= nil and need.focusLevel < 0 then
            include = true
        end

        if include then
            table.insert(items, {
                token = need.token,
                id = need.id,
                focusLevel = need.focusLevel,
                needLevel = need.needLevel,
                deityId = need.deityId,
                isUnmet = need.isUnmet,
                isDeeplyUnmet = need.isDeeplyUnmet
            })
        end
    end

    table.sort(items, function(a, b)
        local a_unmet = a.focusLevel ~= nil and a.focusLevel < 0
        local b_unmet = b.focusLevel ~= nil and b.focusLevel < 0

        if a_unmet ~= b_unmet then
            return a_unmet
        end

        if (a.needLevel or 0) ~= (b.needLevel or 0) then
            return (a.needLevel or 0) > (b.needLevel or 0)
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return shallow_copy_array(items, limit)
end

local function build_prompt_candidates(skills_result, personality_result)
    local traits_result = personality_result.traits or { items = {} }
    local values_result = personality_result.values or { items = {} }
    local needs_result = personality_result.needs or { items = {} }
    local emotions_result = personality_result.emotions or { items = {} }

    local active_emotions = shallow_copy_array(emotions_result.items or {})

    table.sort(active_emotions, function(a, b)
        local a_strength = abs_number(a.relativeStrength or a.strength or 0)
        local b_strength = abs_number(b.relativeStrength or b.strength or 0)

        if a_strength ~= b_strength then
            return a_strength > b_strength
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return {
        topSkills = build_top_skills(skills_result, 12),
        extremeTraits = build_extreme_traits(traits_result, 16),
        strongValues = build_strong_values(values_result, 12),
        strongNeeds = build_strong_needs(needs_result, 12),
        activeEmotions = shallow_copy_array(active_emotions, 12),
        notes = {
            extremeTraits = "traits whose raw value differs from neutral 50 by at least 10",
            strongNeeds = "needs with needLevel >= 2 or currently unmet focusLevel",
            strongValues = "values sorted by absolute strength; polarity is raw sign, not yet interpreted as approve/disapprove prose"
        }
    }
end

local function main(...)
    local args = {...}
    local unit_id = args[1]

    local result = {
        schemaVersion = "fortress-souls-probe-dwarf-soul.v0.2",
        worldLoaded = dfhack.isWorldLoaded(),
        mapLoaded = dfhack.isMapLoaded(),
        siteLoaded = dfhack.isSiteLoaded(),
        tickCount = dfhack.getTickCount(),
        requestedUnitId = unit_id
    }

    if not dfhack.isMapLoaded() then
        result.error = {
            code = "NO_MAP_LOADED",
            message = "DFHack is reachable, but no fortress map is loaded."
        }

        print(json.encode(result, { pretty = false }))
        return
    end

    if unit_id == nil then
        result.error = {
            code = "MISSING_UNIT_ID",
            message = "Expected unit id argument."
        }

        print(json.encode(result, { pretty = false }))
        return
    end

    local unit = find_citizen_by_id(unit_id)

    if unit == nil then
        result.error = {
            code = "UNIT_NOT_FOUND",
            message = "No current citizen found with requested unit id."
        }

        print(json.encode(result, { pretty = false }))
        return
    end

    local soul = nil

    if unit.status ~= nil then
        soul = unit.status.current_soul
    end

    result.identity = summarize_identity(unit)
    result.flags = summarize_flags(unit)
    result.work = summarize_work(unit)
    result.stress = summarize_stress(unit)
    result.soulPresent = soul ~= nil

    if soul == nil then
        result.error = {
            code = "NO_CURRENT_SOUL",
            message = "Unit has no current soul; skills/personality cannot be read."
        }

        print(json.encode(result, { pretty = false }))
        return
    end

    result.skills = summarize_skills(unit, soul)
    result.personality = summarize_personality(unit, soul)
    result.promptCandidates = build_prompt_candidates(result.skills, result.personality)

    print(json.encode(result, { pretty = false }))
end

main(...)