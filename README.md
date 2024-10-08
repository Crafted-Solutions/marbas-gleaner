# marbas-gleaner
Serialization and synchronization tool for MarBas system

## Building
Execute in the solution directory
```sh
dotnet build
```

## Building Releasable Binaries
Execute in the solution directory, replacing `TARGET_OS` with one of the following: `linux-x64`, `win-x64`, `osx-arm64` or `osx-x64`
```sh
dotnet publish src/MarBasGleaner/MarBasGleaner.csproj -p:PublishProfile=TARGET_OS
```
Binaries will be published to `distr` directory.

## Running
Either execute in the solution directory
```sh
dotnet run --project src/MarBasGleaner/MarBasGleaner.csproj -- <ARGUMENTS>
```
Or build binary of your choice (like described in [Building Releasable Binaries]) change to the release directory and run
```sh
mbglean <ARGUMENTS>
```
### Program Arguments
Available arguments and their description can be viewed by executing the program with `-?`, `-h` or `--help` option.

#### mbglean track
```
Sets up tracking of MarBas grains in local directory

Usage:
  mbglean track <url> <path-or-id> [options]

Arguments:
  <url>         Broker URL
  <path-or-id>  Identifier of the top grain to track

Options:
  -d, --directory <directory>                                 Local directory containing tracking information [default: .]
  --auth <auth>                                               Authentication type to use with MarBas broker connection [default: Basic]
  --adopt-checkpoint <adopt-checkpoint>                       Adopt specified checkpoint (-1 for latest) as current one for this connection [default: 0]
  -s, --scope <Anchor|Children|Descendants|Family|Recursive>  Tracking scope [default: Recursive]
  -c, --scs <Git|None|Svn|Tfvc>                               Source control system used for snapshots (currently only Git is supported) [default: Git]
  --ignore-grains <ignore-grains>                             List of grain IDs to ignore
  --ignore-types <ignore-types>                               List of type IDs of grains to ignore
  --ignore-type-names <ignore-type-names>                     List of type names of grains to ignore
```

#### mbglean connect
```
Connects a tracking snapshot with MarBas broker instance

Usage:
  mbglean connect <url> [options]

Arguments:
  <url>  Broker URL

Options:
  -d, --directory <directory>            Local directory containing tracking information [default: .]
  --auth <auth>                          Authentication type to use with MarBas broker connection [default: Basic]
  --adopt-checkpoint <adopt-checkpoint>  Adopt specified checkpoint (-1 for latest) as current one for this connection [default: 0]
```

#### mbglean status
```
Shows status of MarBas grains in a tracking snapshot

Usage:
  mbglean status [options]

Options:
  -d, --directory <directory>  Local directory containing tracking information [default: .]
  --show-all                   List all grains, even unmodified ones
  --assume-reset               Assume broker has been reset since last sync
```

#### mbglean diff
```
Shows differences between grains in snapshot and MarBas broker

Usage:
  mbglean diff <grain-ids>... [options]

Arguments:
  <grain-ids>  ID of the grain to use as comparison base, single argument version compares snapshot with broker, 2 different IDs indicate snapshot version of both by defaulft (s. --mode option)

Options:
  -d, --directory <directory>                                        Local directory containing tracking information [default: .]
  -m, --mode <Auto|Broker|Broker2Snapshot|Snapshot|Snapshot2Broker>  Comparison mode applied to grains identified by <grain-ids> argument, Snapshot2Broker is automatic mode for single ID, Snapshot - for 2 distinct IDs [default: Auto]
```

#### mbglean pull
```
Pulls modified and new grains from MarBas broker into snapshot

Usage:
  mbglean pull [options]

Options:
  -d, --directory <directory>  Local directory containing tracking information [default: .]
  -o, --overwrite              Always overwrite grains in snapshot even newer ones
  --force-checkpoint           Create new checkpoint even when it's safe using latest existing one
```

#### mbglean push
```
Pushes most recent grains from snapshot into MarBas broker

Usage:
  mbglean push [options]

Options:
  -d, --directory <directory>                                                                   Local directory containing tracking information [default: .]
  -c, --starting-checkpoint <starting-checkpoint>                                               Checkpoint number to start operation with, -1 for latest [default: -1]
  -s, --strategy <Ignore|Merge|MergeSkipNewer|Overwrite|OverwriteRecursive|OverwriteSkipNewer>  Strategy for handling grains existing on both sides - in the snapshot and broker [default: OverwriteSkipNewer]
```

## Using NuGet Packages
1. Generate your GitHub personal access token [here](https://github.com/login?return_to=https%3A%2F%2Fgithub.com%2Fsettings%2Ftokens) with **read:packages** permission.
1. Add https://nuget.pkg.github.com/Crafted-Solutions/index.json repository to your **local** `nuget.config`:
    ```xml
    <packageSources>
        <add key="crafted-solutions" value="https://nuget.pkg.github.com/Crafted-Solutions/index.json"/>
    </packageSources>
    <packageSourceCredentials>
        <crafted-solutions>
            <add key="Username" value="YOUR_USER_NAME"/>
            <add key="ClearTextPassword" value="YOUR_PACKAGE_TOKEN"/>
        </crafted-solutions>
    </packageSourceCredentials>
    ```
    Alternatively run this command
    ```sh
    dotnet nuget add source https://nuget.pkg.github.com/Crafted-Solutions/index.json -n crafted-solutions -u YOUR_USER_NAME -p YOUR_PACKAGE_TOKEN --store-password-in-clear-text
    ```
    Alternatively in Visual Studio go to �Tools� -> �Options� -> �NuGet Package Manager� -> �Package Sources� and add the repository as new source.
    
    *DON'T COMMIT ANY CONFIGURATION CONTAINING TOKENS!*