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
mbgleaner <ARGUMENTS>
```
### Program Arguments
Available arguments and their description can be viewed by executing the program with `-?`, `-h` or `--help` option.

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
    Alternatively in Visual Studio go to “Tools” -> “Options” -> “NuGet Package Manager” -> “Package Sources” and add the repository as new source.
    
    *DON'T COMMIT ANY CONFIGURATION CONTAINING TOKENS!*