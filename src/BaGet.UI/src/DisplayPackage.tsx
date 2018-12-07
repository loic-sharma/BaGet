import { HtmlRenderer, Parser } from 'commonmark';
import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import { Tab, Tabs } from 'react-bootstrap';
import timeago from 'timeago.js';
import './DisplayPackage.css';
import LicenseInfo from './LicenseInfo';
import SourceRepository from './SourceRepository';

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

    this.state = { package: undefined };
  }

  public componentDidMount() {
    const url = `v3/registration/${this.props.id}/index.json`;

    fetch(url).then(response => {
      return response.json();
    }).then(json => {
      const results = json as IRegistrationIndex;

      const latestVersion = results.items[0].upper;
      let latestItem: IRegistrationPageItem | undefined;

      const versions: IPackageVersion[] = [];
      const dependencyGroups: IDependencyGroup[] = [];

      for (const entry of results.items[0].items) {
        versions.push({
          date: new Date(entry.catalogEntry.published),
          downloads: entry.catalogEntry.downloads,
          version: entry.catalogEntry.version,
        });

        dependencyGroups.push(...entry.catalogEntry.dependencyGroups);

        if (entry.catalogEntry.version === latestVersion) {
          latestItem = entry;
        }
      }

      if (latestItem) {
        let readme = "";
        if (!latestItem.catalogEntry.hasReadme) {
          readme = latestItem.catalogEntry.description;
        }

        this.setState({
          package: {
            authors: latestItem.catalogEntry.authors,
            dependencyGroups,
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
          const readmeUrl = `v3/package/${this.props.id}/${latestVersion}/readme`;

          fetch(readmeUrl).then(response => {
            return response.text();
          }).then(result => {
            this.setState(prevState => {
              const state = { ...prevState };
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

            <Tabs id="package-commands" animation={false}>
              <Tab eventKey="dotnet-cli" title=".NET CLI" /*t1itle="Switch to tab panel which contains package installation command for .NET CLI"*/>
                <div>
                  <div className="install-script" id="dotnet-cli-text">
                    <span>
                      dotnet add package Newtonsoft.Json --version 12.0.1
                    </span>
                  </div>
                </div>
              </Tab>
              <Tab eventKey="package-manager" title="Package Manager" /*t1itle="Switch to tab panel which contains package installation command for Package Manager"*/>
                <div>
                  <div className="install-script" id="package-manager-text">
                    <span>
                      Install-Package Newtonsoft.Json -Version 12.0.1
                    </span>
                  </div>
                </div>
              </Tab>
              <Tab eventKey="paket" title="Paket CLI" /*t1itle="Switch to tab panel which contains package installation command for Paket"*/>
                <div>
                  <div className="install-script" id="paket-cli-text">
                    <span>
                      paket add Newtonsoft.Json --version 12.0.1
                    </span>
                  </div>
                </div>
              </Tab>
            </Tabs>
            <div className={this.state.package.readme ? "" : "hidden"}>
              <h4>Readme:</h4>
              <pre>{this.state.package.readme}</pre>
            </div>

            <div className={this.state.package.dependencyGroups.length ? "" : "hidden"}>
              <h4>Dependencies:</h4>

              {this.state.package.dependencyGroups.map((depGroup, i) => (
                <div key={`dg${i}`}>
                  <h4>{depGroup.targetFramework}</h4>
                  <ul>
                    {depGroup.dependencies.map((dep, j) => (
                      <li key={`dep${j}`}>
                        {dep.id} {dep.range}
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
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
        </div >
      );
    }
  }

  private dateToString(date: Date): string {
    return `${date.getMonth() + 1}/${date.getDate()}/${date.getFullYear()}`;
  }
}

export default DisplayPackage;