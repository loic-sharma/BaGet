import * as React from 'react';
import timeago from 'timeago.js';

import './DisplayPackage.css';

interface DisplayPackageProps {
  id: string;
}

interface Package {
  id: string;
  latestVersion: string;
  readme: string;
  lastUpdate: Date;
  iconUrl: string;
  projectUrl: string;
  licenseUrl: string;
  downloadUrl: string;
  totalDownloads: number;
  latestDownloads: number;
  authors: string;
  tags: string[];
  versions: PackageVersion[];
}

interface PackageVersion {
  version: string;
  downloads: number;
  date: Date;
}

interface DisplayPackageState {
  package?: Package;
}

export default class DisplayPackage extends React.Component<DisplayPackageProps, DisplayPackageState> {

  constructor(props: DisplayPackageProps) {
    super(props);

    this.state = {package: undefined};
  }

  componentDidMount() {
    let url = `/v3/registration/${this.props.id}/index.json`;

    fetch(url).then(response => {
      return response.json();
    }).then(results => {
      console.log(results);

      let id = results["items"][0]["id"];
      let latestVersion = results["items"][0]["upper"];
      let latestCatalogEntry: {[key: string]: any} | undefined;
      let latestDownloadUrl: string | undefined;

      let versions: PackageVersion[] = [];

      for (let entry of results["items"][0]["items"]) {
        let catalogEntry = entry["catalogEntry"];

        versions.push({
          version: catalogEntry["version"],
          downloads: 0, // TODO
          date: new Date(catalogEntry["published"])
        });

        if (catalogEntry["version"] == latestVersion) {
          latestCatalogEntry = catalogEntry;
          latestDownloadUrl = entry["packageContent"];
        }
      }

      if (latestCatalogEntry && latestDownloadUrl) {
        this.setState({
          package: {
            id: id,
            latestVersion: latestVersion,
            readme: latestCatalogEntry["description"],
            lastUpdate: new Date(latestCatalogEntry["published"]),
            iconUrl: latestCatalogEntry["iconUrl"],
            projectUrl: latestCatalogEntry["projectUrl"],
            licenseUrl: latestCatalogEntry["licenseUrl"],
            downloadUrl: latestDownloadUrl,
            totalDownloads: 0, // TODO
            latestDownloads: 0, // TODO
            authors: latestCatalogEntry["authors"],
            tags: latestCatalogEntry["tags"],
            versions: versions,
          }
        });
      }
    });
  }

  render() {
    if (!this.state.package) {
        return (
          <div>...</div>
        );
    } else {
      return (
        <div className="row display-package">
          <aside className="col-sm-1">
            <img src={this.state.package.iconUrl} className="img-responsive" />
          </aside>
          <article className="col-sm-8 package-details-main">
            <div>
              <h1>{this.state.package.id}</h1>
              <span>{this.state.package.latestVersion}</span>
            </div>
            <div className="install-script">
              dotnet add package {this.state.package.id} --version {this.state.package.latestVersion}
            </div>
            <div>
              {this.state.package.readme}
            </div>
          </article>
          <aside className="col-sm-3 package-details-info">
            <div>
              <h1>Info</h1>

              <div>last updated {timeago().format(this.state.package.lastUpdate)}</div>
              <div><a href={this.state.package.projectUrl}>{this.state.package.projectUrl}</a></div>
              <div><a href={this.state.package.licenseUrl}>License Info</a></div>
            </div>

            <div>
              <h1>Statistics</h1>

              <div>{this.state.package.totalDownloads} total downloads</div>
              <div>{this.state.package.latestDownloads} downloads of latest version</div>
            </div>

            <div>
              <h1>Versions</h1>

              {this.state.package.versions.map(value => (
                <div key={value.version}>
                  <span>v{value.version}: </span>
                  <span>{this._dateToString(value.date)}</span>
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

  private _dateToString(date: Date): string {
      return `${date.getMonth()+1}/${date.getDate()}/${date.getFullYear()}`;
  }
}