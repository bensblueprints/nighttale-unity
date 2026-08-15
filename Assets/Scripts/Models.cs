using System;
using System.Collections.Generic;

namespace NightTale
{
    [Serializable]
    public class GameInfo
    {
        public string slug;
        public string title;
        public string subtitle;
        public string cover;
        public string mode;
        public bool coming_soon;
        public string category;
        public string engine_label;
    }

    [Serializable]
    public class GameMeta
    {
        public string title;
        public string subtitle;
    }

    [Serializable]
    public class ButtonInfo
    {
        public string label;
        public string action;
        public string description;
    }

    [Serializable]
    public class RollRequired
    {
        public bool roll_required;
        public string stat;
        public int dc;
        public int modifier;
        public int score;
    }

    [Serializable]
    public class UserState
    {
        public string username;
        public string email;
        public string name;
        public int credits;
        public int free_turns_remaining;
        public bool subscribed;
        public bool is_super_user;
        public bool can_play;
        public bool email_verified;
        public string lora_status;
        public bool has_custom_lora;
        public string voice_pref;
        public int ads_left_today;
    }

    [Serializable]
    public class OddsBands
    {
        public double? success;
        public double? mixed;
        public double? failure;
    }

    [Serializable]
    public class OddsInfo
    {
        public string check_label;
        public string label;
        public string reason;
        public OddsBands bands;
    }

    [Serializable]
    public class StageInfo
    {
        public string title;
        public string goal;
    }

    [Serializable]
    public class StageChangedInfo
    {
        public string title;
    }

    [Serializable]
    public class StageRollInfo
    {
        public int? value;
        public string band;
    }

    [Serializable]
    public class RollResultInfo
    {
        public string stat;
        public int roll;
        public int modifier;
        public int dc;
        public bool success;
    }

    [Serializable]
    public class DiceInfo
    {
        public List<int> rolls;
        public int? value;
    }

    [Serializable]
    public class StageStatInfo
    {
        public string key;
        public string label;
        public object value;
    }

    [Serializable]
    public class StoryResponse
    {
        public string session_id;
        public string node_id;
        public string story;
        public string raw_story;
        public string image;
        public string portrait;
        public bool image_pending;
        public bool portrait_pending;
        public bool completed;
        public List<ButtonInfo> buttons;
        public List<string> choices;
        public int health;
        public int max_health;
        public int gold;
        public List<string> inventory;
        public string location;
        public Dictionary<string, object> stats;
        public UserState state;
        public GameMeta game;
        public RollRequired roll_required;
        public string error;
        public string message;
        public int? guest_turns_left;
        public string language;

        // Stage-engine extras the HTML5 client renders.
        public OddsInfo odds;
        public StageInfo stage;
        public StageChangedInfo stage_changed;
        public StageRollInfo stage_roll;
        public RollResultInfo roll_result;
        public DiceInfo dice;
        public Dictionary<string, object> deltas;
        public object ending;
        public List<StageStatInfo> stage_stats;
        public List<StageStatInfo> stage_sheet;
    }

    [Serializable]
    public class AdSlotResponse
    {
        public string slot_id;
        public double expires;
        public string ad_url;
        public string ad_type;
        public int view_seconds;
        public int reward_turns;
        public int ads_left_today;
        public string error;
    }
}
