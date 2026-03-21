using McServerManager.Models;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

public sealed class ServerSettingsViewModel : ObservableObject
{
    private int _serverPort;
    private int _maxPlayers;
    private string _motd = string.Empty;
    private bool _onlineMode;
    private bool _enableCommandBlock;
    private string _difficulty = "easy";
    private string _gameMode = "survival";
    private bool _pvp;
    private int _viewDistance;
    private int _spawnProtection;
    private string _levelName = string.Empty;
    private string _seed = string.Empty;
    private ServerProperties _original = new();
    private bool _isDirty;

    public int ServerPort
    {
        get => _serverPort;
        set
        {
            if (SetProperty(ref _serverPort, value))
            {
                UpdateDirty();
            }
        }
    }

    public int MaxPlayers
    {
        get => _maxPlayers;
        set
        {
            if (SetProperty(ref _maxPlayers, value))
            {
                UpdateDirty();
            }
        }
    }

    public string Motd
    {
        get => _motd;
        set
        {
            if (SetProperty(ref _motd, value))
            {
                UpdateDirty();
            }
        }
    }

    public bool OnlineMode
    {
        get => _onlineMode;
        set
        {
            if (SetProperty(ref _onlineMode, value))
            {
                UpdateDirty();
            }
        }
    }

    public bool EnableCommandBlock
    {
        get => _enableCommandBlock;
        set
        {
            if (SetProperty(ref _enableCommandBlock, value))
            {
                UpdateDirty();
            }
        }
    }

    public string Difficulty
    {
        get => _difficulty;
        set
        {
            if (SetProperty(ref _difficulty, value))
            {
                UpdateDirty();
            }
        }
    }

    public string GameMode
    {
        get => _gameMode;
        set
        {
            if (SetProperty(ref _gameMode, value))
            {
                UpdateDirty();
            }
        }
    }

    public bool Pvp
    {
        get => _pvp;
        set
        {
            if (SetProperty(ref _pvp, value))
            {
                UpdateDirty();
            }
        }
    }

    public int ViewDistance
    {
        get => _viewDistance;
        set
        {
            if (SetProperty(ref _viewDistance, value))
            {
                UpdateDirty();
            }
        }
    }

    public int SpawnProtection
    {
        get => _spawnProtection;
        set
        {
            if (SetProperty(ref _spawnProtection, value))
            {
                UpdateDirty();
            }
        }
    }

    public string LevelName
    {
        get => _levelName;
        set
        {
            if (SetProperty(ref _levelName, value))
            {
                UpdateDirty();
            }
        }
    }

    public string Seed
    {
        get => _seed;
        set
        {
            if (SetProperty(ref _seed, value))
            {
                UpdateDirty();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public void Load(ServerProperties properties)
    {
        _original = properties;
        ServerPort = properties.ServerPort;
        MaxPlayers = properties.MaxPlayers;
        Motd = properties.Motd;
        OnlineMode = properties.OnlineMode;
        EnableCommandBlock = properties.EnableCommandBlock;
        Difficulty = properties.Difficulty;
        GameMode = properties.GameMode;
        Pvp = properties.Pvp;
        ViewDistance = properties.ViewDistance;
        SpawnProtection = properties.SpawnProtection;
        LevelName = properties.LevelName;
        Seed = properties.Seed;
        IsDirty = false;
    }

    public ServerProperties ToModel()
    {
        return new ServerProperties
        {
            ServerPort = ServerPort,
            MaxPlayers = MaxPlayers,
            Motd = Motd,
            OnlineMode = OnlineMode,
            EnableCommandBlock = EnableCommandBlock,
            Difficulty = Difficulty,
            GameMode = GameMode,
            Pvp = Pvp,
            ViewDistance = ViewDistance,
            SpawnProtection = SpawnProtection,
            LevelName = LevelName,
            Seed = Seed
        };
    }

    private void UpdateDirty()
    {
        IsDirty =
            _original.ServerPort != ServerPort ||
            _original.MaxPlayers != MaxPlayers ||
            _original.Motd != Motd ||
            _original.OnlineMode != OnlineMode ||
            _original.EnableCommandBlock != EnableCommandBlock ||
            _original.Difficulty != Difficulty ||
            _original.GameMode != GameMode ||
            _original.Pvp != Pvp ||
            _original.ViewDistance != ViewDistance ||
            _original.SpawnProtection != SpawnProtection ||
            _original.LevelName != LevelName ||
            _original.Seed != Seed;
    }
}
