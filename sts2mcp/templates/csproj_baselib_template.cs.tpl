<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <RootNamespace>{namespace}</RootNamespace>
    <AssemblyName>{assembly_name}</AssemblyName>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Sts2Dir>{sts2_data_dir}</Sts2Dir>
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
</Project>
