using System;
using System.Collections.Generic;
using System.Linq;
using Babylon.ResourcesProvider;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace JsonResourceProvider
{
    /// <summary>
    /// XML Resource Provider for Babylon.NET
    /// 
    /// The provider will read all XML files in the base directory and treat them as files containing string resources.Files are named
    /// using the pattern<filename>.[culture code].xml.The provider assumes invariant strings are contained in a file with no culture
    /// code in the file name(e.g.strings.xml). All files containing culture codes(e.g.strings.de-DE.xml) will be treated as translations.
    /// 
    /// Strings not present in the invariant file are ignored.
    /// 
    /// Relative paths are fully supported.Subfolders of the base directory are also processed.The name of the subfolder becomes part
    /// of the resource name and therefore all translations of an invariant file must be placed in the same folder.
    /// 
    /// Comments are supported.
    /// </summary>
    public class XmlResourcesProvider : IResourcesProvider
    {
        string _storageLocation;

        /// <summary>
        /// The StorageLocation will be set by the user when creating a new generic localization project in Babylon.NET. It can be a path to a folder, a file name,
        /// a database connection string or any other information needed to access the resource files.
        /// </summary>
        public string StorageLocation
        {
            get
            {
                return _storageLocation;
            }

            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(value);

                _storageLocation = value;
            }
        }

        /// <summary>
        /// This text is displayed to the user as label to the storage location textbox/combobox when setting up the resource provider.
        /// </summary>
        public string StorageLocationUserText
        {
            get
            {
                return "Base Directory where language files are located";
            }
        }

        /// <summary>
        /// This is the type of storage used be the provider. Depending on the type Babylon.NET will display a FileSelectionControl, a DirectorySelectionControl 
        /// or a simple TextBox as StorageLocation input control.
        /// </summary>
        public StorageType StorageType
        {
            get
            {
                return StorageType.Directory;
            }
        }

        /// <summary>
        /// This is the description of the Resource Provider Babylon.NET will display when selecting a Resource Provider
        /// </summary>
        public string Description
        {
            get
            {
                return "Standard XML Resources Provider. Every XML file contains one language.";
            }
        }

        /// <summary>
        /// This is the name of the Resource Provider Babylon.NET will display when selecting a Resource Provider
        /// </summary>
        public string Name
        {
            get
            {
                return "XML Resources Provider";
            }
        }

        /// <summary>
        /// Babylon.NET will pass the path to the current solution to the provider. This can for example be used to work with relative paths.
        /// </summary>
        public string SolutionPath { get; set; }

        /// <summary>
        /// Babylon.NET will call this method when the resource files should be written.
        /// </summary>
        /// <param name="projectName">Name of the project whose resources are exported.</param>
        /// <param name="resourceStrings">A list of resource strings with related translations.</param>
        /// <param name="resultDelegate">Delegate to return the status of the export.</param>
        public void ExportResourceStrings(string projectName, ICollection<StringResource> resourceStrings, ResourceStorageOperationResultDelegate resultDelegate)
        {
            // We use a dictionary as cache for the resources for each file
            Dictionary<string, ICollection<StringResource>> fileCache = new Dictionary<string, ICollection<StringResource>>();

            // We keep an error list with files that cannot be written to avoid the same error over and over
            List<string> errorList = new List<string>();
     
            // convert relative storage location into absolute one
            string baseDirectory = GetBaseDirectory();
                 
            // loop over all strings...
            foreach (var resString in resourceStrings)
            {
                // ... and all locales. Babylon.NET uses an empty string as locale for the invariant language.
                foreach (string locale in resString.GetLocales())
                {
                    // assemble file name
                    string filename = Path.Combine(baseDirectory, string.Format("{0}.{1}.xml", resString.StorageLocation, locale)).Replace("..", ".");

                    // if we have this file on the error list skip it
                    if (errorList.Contains(filename))
                    {
                        continue;
                    }

                    // check if we have the file in our cache
                    if (!fileCache.ContainsKey(filename))
                    {
                        // load strings from file if file exists 
                        if (File.Exists(filename))
                        {
                            try
                            {
                                var strings = ReadResourceStrings(filename);
                                fileCache.Add(filename, strings);
                            }
                            catch (Exception ex)
                            {
                                if (resultDelegate != null)
                                {
                                    ResourceStorageOperationResultItem resultItem = new ResourceStorageOperationResultItem(filename);
                                    resultItem.ProjectName = projectName;
                                    resultItem.Result = ResourceStorageOperationResult.Error;
                                    resultItem.Message = ex.GetBaseException().Message;
                                    resultDelegate(resultItem);
                                }

                                errorList.Add(filename);

                                continue;
                            }
                        }
                        else
                        {
                            // create new string list for new file
                            var strings = new List<StringResource>();
                            fileCache.Add(filename, strings);
                        }
                    }

                    // update the string
                    var stringResources = fileCache[filename];
                    var s = stringResources.FirstOrDefault(sr => sr.Name == resString.Name);
                    if (s == null)
                    {
                        s = new StringResource(resString.Name, "");
                        stringResources.Add(s);    
                    }

                    s.SetLocaleText(locale, resString.GetLocaleText(locale));
                    s.Notes = resString.Notes;
                }
            }

            // save all dictionaries in cache
            foreach (var item in fileCache)
            {
                ResourceStorageOperationResultItem resultItem = new ResourceStorageOperationResultItem(item.Key);
                resultItem.ProjectName = projectName;

                // get locale from file name
                string locale = Path.GetExtension(Path.GetFileNameWithoutExtension(item.Key)).TrimStart(new char[] { '.' });

                try
                {
                    WriteResourceStrings(item, locale);

                    // report success
                    resultDelegate?.Invoke(resultItem);
                }
                catch (Exception ex)
                {
                    // report error
                    if (resultDelegate != null)
                    {
                        resultItem.Result = ResourceStorageOperationResult.Error;
                        resultItem.Message = ex.GetBaseException().Message;
                        resultDelegate(resultItem);
                    }
                }
            }
        }

        /// <summary>
        /// Called by Babylon.NET when synchronizing a project with the respective resource files.
        /// </summary>
        /// <param name="projectName">Name of the project whose resources are exported.</param>
        /// <returns></returns>
        public ICollection<StringResource> ImportResourceStrings(string projectName)
        {
            // We use a Dictionary to keep a list of all StringResource object searchable by the key.
            Dictionary<string, StringResource> workingDictionary = new Dictionary<string, StringResource>();

            // convert relative storage location into absolute one
            string baseDirectory = GetBaseDirectory();

            // iterate over the whole folder tree starting from the base directory.
            foreach (var file in Directory.EnumerateFiles(baseDirectory, "*.xml", SearchOption.AllDirectories))
            {
                // get locale from file name
                string locale = Path.GetExtension(Path.GetFileNameWithoutExtension(file)).TrimStart(new char[] { '.' });

                XmlDocument doc = new XmlDocument();
                doc.Load(file);
                string xpath = "/strings/string";
                XmlNodeList nodes = doc.SelectNodes(xpath);
                foreach (XmlNode node in nodes)
                {
                    if (node.Attributes["key"] == null)
                    {
                        throw new ApplicationException("Invalid XML file, 'key' attribute not found!");
                    }

                    string key = node.Attributes["key"].InnerText;
                    string comment = node.Attributes["comment"]?.InnerText;
                    string value = node?.InnerText;

                    StringResource stringRes;
                    string relativeDirectory = Path.GetDirectoryName(file.Replace(baseDirectory, "")).TrimStart(Path.DirectorySeparatorChar); 
                    string plainFilename = Path.Combine(relativeDirectory, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(Path.GetFileName(file))));

                    // check whether we already have this string 
                    if (!workingDictionary.TryGetValue(plainFilename + key, out stringRes))
                    {
                        stringRes = new StringResource(key, "");
                        stringRes.StorageLocation = plainFilename;
                        workingDictionary.Add(plainFilename + key, stringRes);
                    }

                    // add locale text. Babylon.NET uses an empty string as locale for the invariant language. A StringResource is only valid if the invariant language is set. 
                    // StringResources without an invariant language text are discared by Babylon.NET.
                    stringRes.SetLocaleText(locale, value);

                    // add comment
                    stringRes.Notes = comment;
                }
            }

            // get collection of stringResources
            List<StringResource> result = new List<StringResource>();
            workingDictionary.ToList().ForEach(i => result.Add(i.Value));
            return result;
        }

        private static void WriteResourceStrings(KeyValuePair<string, ICollection<StringResource>> item, string locale)
        {
            var stringsElem = new XElement("strings");

            foreach (var s in item.Value)
            {
                var stringElem = new XElement("string");
                stringElem.Add(new XAttribute("key", s.Name));
                if (s.Notes != null)
                {
                    stringElem.Add(new XAttribute("comment", s.Notes));
                }
                stringsElem.Add(stringElem);
                stringElem.Add(s.GetLocaleText(locale));
            }

            stringsElem.Save(item.Key);
        }

        private ICollection<StringResource> ReadResourceStrings(string filename)
        {
            var result = new List<StringResource>();

            XmlDocument doc = new XmlDocument();
            doc.Load(filename);
            string xpath = "/strings/string";
            XmlNodeList nodes = doc.SelectNodes(xpath);
            foreach (XmlNode node in nodes)
            {
                if (node.Attributes["key"] == null)
                {
                    throw new ApplicationException("Invalid XML file, 'key' attribute not found!");
                }

                string key = node.Attributes["key"].InnerText;
                string comment = node.Attributes["comment"]?.InnerText;
                string value = node?.InnerText;

                StringResource stringRes = new StringResource(key, "");

                // get locale from file name
                string locale = Path.GetExtension(Path.GetFileNameWithoutExtension(filename)).TrimStart(new char[] { '.' });

                // add locale text
                stringRes.SetLocaleText(locale, value);

                // add comment
                stringRes.Notes = comment;

                result.Add(stringRes);
            }

            return result;
        }

        private string GetBaseDirectory()
        {
            string baseDirectory = _storageLocation;
            if (!Path.IsPathRooted(baseDirectory))
            {
                baseDirectory = Path.GetFullPath(Path.Combine(SolutionPath, baseDirectory));
            }

            return baseDirectory;
        }
    }
}
