# polbots
A Reddit bot in Unity that tries to make fun of modern geopolitics with AI generated animations and dialogue.

### config.json
You need this at the executable root path to get started.
```
[
  {
    "Type": "openai",
    "ApiUri": "https://api.openai.com",         // should work with ollama, groq, and grok (untested)
    "ApiKey": ...OPEN AI API KEY...
    "SlowModel": "gpt-4o",                      // used for dialogue generation
    "FastModel": "gpt-4o-mini"                  // used for context generation
  },
  {
    "Type": "tts",
    "GoogleApiKey": ...GOOGLE API KEY...        // only google is supported at the moment
  },
  {
    "Type": "discord",                          // remove any config block to disable it
    "WebhookURL": ...DISCORD WEBHOOK URL...     // this logs subtitles to discord
  },
  {
    "Type": "folder",                           // play all pre-generated scenes from a folder
    "Prompts": [],
    "ReplayDirectory": "polbots",
    "ReplayRate": 80,                           // can repeat replay AT LEAST every x times
    "ReplaysPerBatch": 20,                      // adds x replays at a time
    "MaxReplayAgeInMinutes": 1440,
    "AutoPlay": false
  },
  {
    "Type": "reddit",
    "SubReddits": [
      "worldnews+anime_titties+todayilearned",  // remove the r/ prefix
      "AskHistorians+UnitedNations+geopolitics",// you can join multiple subreddits with '+'
      "Africa+China+america+australia+europe"   // you can add /new or /hot etc to end
    ],
    "PostsPerIdea": 6,
    "MaxPostAgeInHours": 24,
    "BatchMax": 5,                              // grabs x posts at a time to generate
    "BatchLifetimeMax": 100,                    // generates x posts in entire lifetime before quitting
    "BatchPeriodInMinutes": 480                 // generates new batch every x minutes
  },
  {
    "Type": "obs",                              // again, remove entire config block to disable
    "OBSWebSocketURI": "ws://localhost:4455",
    "IsStreaming": false,                       // streams until no more new episodes
    "IsRecording": false,                       // records until no more new episodes
    "DoSplitRecording": true                    // splits scenes into individual videos (~2.5 mins)
  }
]
```