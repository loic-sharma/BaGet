export interface IRegistrationIndex {
  totalDownloads: number;
  items: IRegistrationPage[];
}

export interface IRegistrationPage {
  id: string;
  lower: string;
  upper: string;
  items: IRegistrationPageItem[];
}

export interface IRegistrationPageItem {
  packageContent: string;
  catalogEntry: ICatalogEntry;
}

export interface ICatalogEntry {
  id: string;
  version: string;
  downloads: number;
  published: string;
  hasReadme: boolean;
  description: string;
  iconUrl: string;
  projectUrl: string;
  licenseUrl: string;
  releaseNotes: string;
  listed: boolean;
  packageTypes: string[];
  repositoryUrl: string;
  repositoryType?: string;
  authors: string;
  tags: string[];
  dependencyGroups: IDependencyGroup[];
}

export interface IDependencyGroup {
  targetFramework: string;
  dependencies: IDependency[] | undefined;
}

export interface IDependency {
  id: string;
  range: string;
}
