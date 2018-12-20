import { HtmlRenderer, Parser } from 'commonmark';
import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import timeago from 'timeago.js';
import Dependencies from './Dependencies';
import InstallationInfo from './InstallationInfo';
import LicenseInfo from './LicenseInfo';
import * as Registration from './Registration';
import SourceRepository from './SourceRepository';

import './DisplayPackage.css';

interface IDisplayPackageProps {
  match: {
    params: {
      id: string;
    }
  }
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
  dependencyGroups: Registration.IDependencyGroup[];
}

interface IPackageVersion {
  version: string;
  downloads: number;
  date: Date;
}

interface IDisplayPackageState {
  package?: IPackage;
}

class DisplayPackage extends React.Component<IDisplayPackageProps, IDisplayPackageState> {

  private id: string;
  private parser: Parser;
  private htmlRenderer: HtmlRenderer;

  constructor(props: IDisplayPackageProps) {
    super(props);

    this.parser = new Parser();
    this.htmlRenderer = new HtmlRenderer();

    this.id = props.match.params.id;
    this.state = {package: undefined};
  }

  public componentDidMount() {
    const url = `/v3/registration/${this.id}/index.json`;

    fetch(url).then(response => {
      return response.json();
    }).then(json => {
      const results = json as Registration.IRegistrationIndex;

      const latestVersion = results.items[0].upper;
      let latestItem: Registration.IRegistrationPageItem | undefined;

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
          const readmeUrl = `/v3/package/${this.id}/${latestVersion}/readme`;

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

            <InstallationInfo id={this.state.package.id} version={this.state.package.latestVersion} />

            {/* TODO: Fix this */}
            <div dangerouslySetInnerHTML={{ __html: this.state.package.readme }} />

            <Dependencies dependencyGroups={this.state.package.dependencyGroups} />
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
