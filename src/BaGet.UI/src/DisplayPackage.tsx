import { HtmlRenderer, Parser } from 'commonmark';
import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import timeago from 'timeago.js';
import LicenseInfo from './LicenseInfo';
import SourceRepository from './SourceRepository';

import './DisplayPackage.css';

interface IDisplayPackageProps {
  id: string;
}

interface IPackage {
  id: string;
  latestVersion: string;
  readme: string;
  lastUpdate: Date;
  iconUrl: string;
  projectUrl: string;
  licenseUrl: string;
  downloadUrl: string;
  repositoryUrl: string;
  repositoryType: string;
  totalDownloads: number;
  latestDownloads: number;
  authors: string;
  tags: string[];
  versions: IPackageVersion[];
  dependencyGroups: IDependencyGroup[];
}

interface IPackageVersion {
  version: string;
  downloads: number;
  date: Date;
}

interface IDisplayPackageState {
  package?: IPackage;
}

interface IRegistrationIndex {
  totalDownloads: number;
  items: IRegistrationPage[];
}

interface IRegistrationPage {
  id: string;
  lower: string;
  upper: string;
  items: IRegistrationPageItem[];
}

interface IRegistrationPageItem {
  packageContent: string;
  catalogEntry: ICatalogEntry;
}

interface ICatalogEntry {
  id: string;
  version: string;
  downloads: number;
  published: string;
  hasReadme: boolean;
  description: string;
  iconUrl: string;
  projectUrl: string;
  licenseUrl: string;
  repositoryUrl: string;
  repositoryType: string;
  authors: string;
  tags: string[];
  dependencyGroups: IDependencyGroup[];
}

interface IDependencyGroup {
  targetFramework: string;
  dependencies: IDependency[];
}

interface IDependency {
  id: string;
  range: string;
}

class DisplayPackage extends React.Component<IDisplayPackageProps, IDisplayPackageState> {

  private parser: Parser;
  private htmlRenderer: HtmlRenderer;

  constructor(props: IDisplayPackageProps) {
    super(props);

    this.parser = new Parser();
    this.htmlRenderer = new HtmlRenderer();

    this.state = {package: undefined};
  }

  public componentDidMount() {
    const url = `/v3/registration/${this.props.id}/index.json`;

    fetch(url).then(response => {
      return response.json();
    }).then(json => {
      const results = json as IRegistrationIndex;

      const latestVersion = results.items[0].upper;
      let latestItem: IRegistrationPageItem | undefined;

      const versions: IPackageVersion[] = [];

      for (const entry of results.items[0].items) {
        versions.push({
          date: new Date(entry.catalogEntry.published),
          downloads: entry.catalogEntry.downloads,
          version: entry.catalogEntry.version,
        });

        if (entry.catalogEntry.version === latestVersion) {
          latestItem = entry;
        }
      }

      if (latestItem) {
        latestItem.catalogEntry.dependencyGroups.map(group => {
          if (!group.dependencies) {
            group.dependencies = [];
          }
        });
        
        let readme = "";
        if (!latestItem.catalogEntry.hasReadme) {
          readme = latestItem.catalogEntry.description; 
        }

        this.setState({
          package: {
            authors: latestItem.catalogEntry.authors,
            dependencyGroups: latestItem.catalogEntry.dependencyGroups,
            downloadUrl: latestItem.packageContent,
            iconUrl: latestItem.catalogEntry.iconUrl,
            id: latestItem.catalogEntry.id,
            lastUpdate: new Date(latestItem.catalogEntry.published),
            latestDownloads: latestItem.catalogEntry.downloads,
            latestVersion,
            licenseUrl: latestItem.catalogEntry.licenseUrl,
            projectUrl: latestItem.catalogEntry.projectUrl,
            readme,
            repositoryType: latestItem.catalogEntry.repositoryType,
            repositoryUrl: latestItem.catalogEntry.repositoryUrl,
            tags: latestItem.catalogEntry.tags,
            totalDownloads: results.totalDownloads,
            versions
          }
        });

        if (latestItem.catalogEntry.hasReadme) {
          const readmeUrl = `/v3/package/${this.props.id}/${latestVersion}/readme`;

          fetch(readmeUrl).then(response => {
            return response.text();
          }).then(result => {
            this.setState(prevState => {
              const state = {...prevState};
              const markdown = this.parser.parse(result);

              state.package!.readme = this.htmlRenderer.render(markdown);

              return state;
            });
          });
        }
      }
    });
  }

  public render() {
    if (!this.state.package) {
        return (
          <div>...</div>
        );
    } else {
      return (
        <div className="row display-package">
          <aside className="col-sm-1 package-icon">
            <img src={this.state.package.iconUrl} className="img-responsive" />
          </aside>
          <article className="col-sm-8 package-details-main">
            <div className="package-title">
              <h1>
                {this.state.package.id}
                <small className="text-nowrap">{this.state.package.latestVersion}</small>
              </h1>

            </div>

            <div className="install-tabs">
              <ul className="nav nav-tabs" role="tablist">

                <li role="presentation" className="active">
                  <a href="#dotnet-cli" aria-expanded="true" aria-selected="true" aria-controls="dotnet-cli" role="tab" data-toggle="tab" title="Switch to tab panel which contains package installation command for .NET CLI">
                    .NET CLI
                  </a>
                </li>

                <li role="presentation">
                  <a href="#package-manager" aria-expanded="false" aria-selected="false" aria-controls="dotnet-cli" role="tab" data-toggle="tab" title="Switch to tab panel which contains package installation command for Package Manager">
                    Package Manager
                  </a>
                </li>

                <li role="presentation">
                  <a href="#paket" aria-expanded="false" aria-selected="false" aria-controls="dotnet-cli" role="tab" data-toggle="tab" title="Switch to tab panel which contains package installation command for Paket">
                    Paket CLI
                  </a>
                </li>

              </ul>
            </div>

            <div className="tab-content">

              <div role="tabpanel" className="tab-pane active" id="dotnet-cli">
                <div>
                  <div className="install-script" id="dotnet-cli-text">
                    <span>
                      dotnet add package Newtonsoft.Json --version 12.0.1
                    </span>
                  </div>
                  <div className="copy-button">
                    <button id="dotnet-cli-button" className="btn btn-default btn-warning" type="button" data-toggle="popover" data-placement="bottom" data-content="Copied." aria-label="Copy the .NET CLI command" data-original-title="" title="">
                      <Icon iconName="Copy" className="ms-Icon" />
                    </button>
                  </div>
                </div>
              </div>

              <div role="tabpanel" className="tab-pane" id="package-manager">
                <div>
                  <div className="install-script" id="package-manager-text">
                    <span>
                        Install-Package Newtonsoft.Json -Version 12.0.1
                    </span>
                  </div>
                  <div className="copy-button">
                    <button id="package-manager-button" className="btn btn-default btn-warning" type="button" data-toggle="popover" data-placement="bottom" data-content="Copied." aria-label="Copy the Package Manager command" data-original-title="" title="">
                      <Icon iconName="Copy" className="ms-Icon" />
                    </button>
                  </div>
                </div>
              </div>

              <div role="tabpanel" className="tab-pane " id="paket-cli">
                <div>
                  <div className="install-script" id="paket-cli-text">
                    <span>
                      paket add Newtonsoft.Json --version 12.0.1
                    </span>
                  </div>
                  <div className="copy-button">
                    <button id="paket-cli-button" className="btn btn-default btn-warning" type="button" data-toggle="popover" data-placement="bottom" data-content="Copied." aria-label="Copy the Paket CLI command" data-original-title="" title="">
                      <Icon iconName="Copy" className="ms-Icon" />
                    </button>
                  </div>
                </div>
              </div>

            </div>

            {/* TODO: Fix this */}
            <div dangerouslySetInnerHTML={{ __html: this.state.package.readme }} />

            <div>
              <h3>Dependencies</h3>

              {this.state.package.dependencyGroups.length > 0 ? (
                <div>
                  {this.state.package.dependencyGroups.map(depGroup => (
                    <div key={depGroup.targetFramework}>
                      <h4>{depGroup.targetFramework}</h4>
                      {depGroup.dependencies.length > 0 ? (
                        <ul>
                          {depGroup.dependencies.map(dep => (
                            <li key={dep.id}>
                              {dep.id} {dep.range}
                            </li>
                          ))}
                        </ul>
                      ) : (
                        <div>No dependencies.</div>
                      )}
                    </div>
                  ))}
                </div>
                ) : (
                <div>This package has no dependencies.</div>
              )}
          </div>
          </article>
          <aside className="col-sm-3 package-details-info">
            <div>
              <h2>Info</h2>

              <ul className="list-unstyled ms-Icon-ul">
                <li>
                  <Icon iconName="History" className="ms-Icon" />
                  Last updated {timeago().format(this.state.package.lastUpdate)}
                </li>
                <li>
                  <Icon iconName="Globe" className="ms-Icon" />
                  <a href={this.state.package.projectUrl}>{this.state.package.projectUrl}</a>
                </li>
                <SourceRepository url={this.state.package.repositoryUrl} type={this.state.package.repositoryType} />
                <LicenseInfo url={this.state.package.licenseUrl} />
                <li>
                  <Icon iconName="CloudDownload" className="ms-Icon" />
                  <a href={this.state.package.downloadUrl}>Download Package</a>
                </li>
              </ul>
            </div>

            <div>
              <h2>Statistics</h2>

              <ul className="list-unstyled ms-Icon-ul">
                <li>
                  <Icon iconName="Download" className="ms-Icon" />
                  {this.state.package.totalDownloads.toLocaleString()} total downloads
                </li>
                <li>
                  <Icon iconName="GiftBox" className="ms-Icon" />
                  {this.state.package.latestDownloads.toLocaleString()} downloads of latest version
                </li>
              </ul>
            </div>

            <div>
              <h2>Versions</h2>

              {this.state.package.versions.map(value => (
                <div key={value.version}>
                  <span>v{value.version}: </span>
                  <span>{this.dateToString(value.date)}</span>
                </div>
              ))}
            </div>

            <div>
              <h1>Authors</h1>

              <p>{(!this.state.package.authors) ? 'Unknown' : this.state.package.authors}</p>
            </div>
          </aside>
        </div>
      );
    }
  }

  private dateToString(date: Date): string {
    return `${date.getMonth()+1}/${date.getDate()}/${date.getFullYear()}`;
  }
}

export default DisplayPackage;