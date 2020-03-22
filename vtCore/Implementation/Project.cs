﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using vtCore.Interfaces;

namespace vtCore
{
    public static class Factory
    {
        public static IProject makeProject(SetupData setup, LibManager libManager)
        {
            return new Project(setup, libManager);
        }
        public static LibManager makeLibManager(SetupData setup)
        {
            return new LibManager(setup);
        }
    }

    internal class Project : IProject
    {    
        public IEnumerable<IConfiguration> configurations => _configurations;
        public IConfiguration selectedConfiguration => _selectedConfiguration;

        public LibManager libManager { get; private set; }

        public Target target { get; set; } = Target.vsCode;
        public BuildSystem buildSystem { get; set; } = BuildSystem.makefile;
        public DebugSupport debugSupport { get; set; } = DebugSupport.none;
        public bool useInoFiles { get; set; } = false;
        
        public string path { get; set; }
        public string pathError
        {
            get
            {
                try
                {
                    Path.GetFullPath(path);
                    return null;
                }
                catch { return "Path to the project folder not valid"; }
            }
        }
        public string libBase => Path.Combine(path, "lib");
        public string name => Path.GetFileName(path ?? "ERROR");
        public string cleanName => name.Replace(" ", "_");
        public string mainSketchPath => (buildSystem == BuildSystem.makefile && !useInoFiles)  ? Path.Combine(path, "src", "main.cpp") : Path.Combine(path, cleanName + ".ino");

        public void newProject()
        {
            // Project Path -------------------------------------
            int i = 1;
            path = Path.Combine(setup.projectBaseDefault, $"newProject");
            while (Directory.Exists(path)) { path = Path.Combine(setup.projectBaseDefault, $"newProject({i++})"); }

            buildSystem = BuildSystem.makefile;
            target = Target.vsCode;
            debugSupport = setup.debugSupportDefault == true ? DebugSupport.cortex_debug : DebugSupport.none;

            // Add a default configuration ----------------------
            _configurations.Clear();
            var sc = Configuration.getDefault(setup);

            _selectedConfiguration = sc;
            _configurations.Add(sc);           

            var dummyConfig = Configuration.getDefault(setup);
            dummyConfig.name = "Testdummy";
            _configurations.Add(dummyConfig);
        }

        public void openProject(string projectPath)
        {
            _configurations.Clear();

            this.path = projectPath;
            string vsTeensyJsonPath = Path.Combine(projectPath, ".vsteensy", "vsteensy.json");


            if (!File.Exists(vsTeensyJsonPath))
            {
                openNewFolder();
            }
            else
            {
                openExistingFolder(vsTeensyJsonPath);
            }
        }

        public Project(SetupData setup, LibManager libManager)
        {
            this.setup = setup;
            this.libManager = libManager;
            this._configurations = new List<IConfiguration>();
        }
                
        private void openNewFolder()
        {
            buildSystem = BuildSystem.makefile;
            target = Target.vsCode;
            debugSupport = DebugSupport.none;

            var sc = Configuration.getDefault(setup);
            _configurations.Add(sc);
            _selectedConfiguration = sc;
        }
        private void openExistingFolder(string vsTeensyJsonPath)
        {
            try
            {  // read vsTeensy.json
                var fileContent = JsonConvert.DeserializeObject<ProjectTransferData>(File.ReadAllText(vsTeensyJsonPath));

                if (fileContent == null || fileContent.version != "1" || fileContent.configurations.Count == 0)
                {
                    openNewFolder();
                }
                else
                {
                    buildSystem = fileContent.buildSystem;
                    target = fileContent.target;
                    debugSupport = fileContent.debugSupport;
                    useInoFiles = fileContent.useInoFile;

                    foreach (var cfg in fileContent.configurations)
                    {
                        var configuration = (Configuration)cfg;
                        configuration.setup = this.setup;       /// hack, look for better solution
                        

                        // add shared libraries ---------------------
                        if (cfg.sharedLibraries != null)
                        {
                            var sharedLibraries = libManager.sharedRepository.libraries.Select(version => version.FirstOrDefault()); //flatten out list by selecting first version. Shared libraries  can only have one version
                            
                            foreach (var cfgSharedLib in cfg.sharedLibraries)
                            {
                                var library = sharedLibraries.FirstOrDefault(lib => lib.sourceFolderName == cfgSharedLib); // find the corresponding lib
                                if (library != null)
                                {
                                    configuration.sharedLibs.Add(ProjectLibrary.cloneFromLib(library));
                                }
                            }
                        }

                        // add libraries installed in ./lib ---------------------                                          
                        var repo = new RepositoryLocal("tmp", libBase);   // Hack, directly store repo instead of lib list???
                        var localLibs = repo.libraries?.Select(version => version.FirstOrDefault());

                        if (localLibs != null)
                        {
                            foreach (var lib in localLibs)
                            {
                                var pl =  ProjectLibrary.cloneFromLib(lib);
                                pl.targetUri = lib.sourceUri;
                                configuration.localLibs.Add(pl);
                            }
                        }

                        // initialize boards list and options
                        configuration.parseBoardsTxt(/*configuration.setupType == SetupTypes.quick ? setup.arduinoBoardsTxt : null*/ null);
                        configuration.selectedBoard = configuration.boards?.FirstOrDefault(b => b.name == cfg.board.name);
                        if (configuration.selectedBoard != null)
                        {
                            foreach (var option in cfg.board.options)
                            {
                                var optionSet = configuration.selectedBoard.optionSets.FirstOrDefault(x => x.name == option.Key);
                                if (optionSet != null)
                                {
                                    optionSet.selectedOption = optionSet.options.FirstOrDefault(x => x.name == option.Value);
                                }
                            }
                        }

                        _configurations.Add(configuration);

                    }
                    _selectedConfiguration = _configurations.FirstOrDefault();
                }

            }
            catch //(Exception ex)
            {
                //log.Warn($"config file {vsTeensyJson} does not exist");
                var sc = Configuration.getDefault(setup);
                _selectedConfiguration = sc;
                _configurations.Add(sc);

                return;
            }
        }
        private List<IConfiguration> _configurations { get; }
        private IConfiguration _selectedConfiguration { get; set; }
        private readonly SetupData setup;
    }
}

