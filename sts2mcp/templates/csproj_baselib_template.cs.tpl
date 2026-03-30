<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <RootNamespace>{namespace}</RootNamespace>
    <AssemblyName>{assembly_name}</AssemblyName>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputPath>bin\$(Configuration)\</OutputPath>
    <AppOutputBase>$(MSBuildProjectDirectory)\</AppOutputBase>
    <Sts2Dir>{sts2_data_dir}</Sts2Dir>
    <ModsPath Condition="'$(ModsPath)' == '' and '$(Sts2Dir)' != ''">$([System.IO.Path]::Combine($([System.IO.Directory]::GetParent('$(Sts2Dir)').FullName), 'mods'))</ModsPath>
    <EnableCopyToModsFolderOnBuild>false</EnableCopyToModsFolderOnBuild>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <Optimize>false</Optimize>
    <DebugType>portable</DebugType>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <Optimize>true</Optimize>
    <DebugType>none</DebugType>
    <PathMap>$(AppOutputBase)=.</PathMap>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="GodotSharp" Version="4.4.0" />
    <PackageReference Include="Lib.Harmony" Version="2.4.2" />
    <PackageReference Include="Alchyr.Sts2.BaseLib" Version="0.1.*" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="sts2">
      <HintPath>$(Sts2Dir)\\sts2.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <Target Name="CopyToModsFolderOnBuild"
          AfterTargets="PostBuildEvent"
          Condition="'$(EnableCopyToModsFolderOnBuild)' == 'true' and '$(ModsPath)' != ''">
    <PropertyGroup>
      <_ModOutputDir>$([System.IO.Path]::Combine('$(ModsPath)', '$(MSBuildProjectName)'))</_ModOutputDir>
    </PropertyGroup>

    <MakeDir Directories="$(_ModOutputDir)" />

    <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
          DestinationFolder="$(_ModOutputDir)"
          Condition="Exists('$(TargetDir)$(TargetName).dll')" />
    <Copy SourceFiles="$(TargetDir)$(TargetName).pdb"
          DestinationFolder="$(_ModOutputDir)"
          Condition="Exists('$(TargetDir)$(TargetName).pdb')" />
    <Copy SourceFiles="$(TargetDir)$(TargetName).deps.json"
          DestinationFolder="$(_ModOutputDir)"
          Condition="Exists('$(TargetDir)$(TargetName).deps.json')" />
    <Copy SourceFiles="$(TargetDir)$(TargetName).runtimeconfig.json"
          DestinationFolder="$(_ModOutputDir)"
          Condition="Exists('$(TargetDir)$(TargetName).runtimeconfig.json')" />
    <Copy SourceFiles="$(MSBuildProjectDirectory)\mod_manifest.json"
          DestinationFolder="$(_ModOutputDir)"
          Condition="Exists('$(MSBuildProjectDirectory)\mod_manifest.json')" />
  </Target>
</Project>
