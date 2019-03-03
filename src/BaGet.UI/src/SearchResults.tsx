import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import { Link } from 'react-router-dom';
import './SearchResults.css';

interface ISearchResultsProps {
  input: string;
}

interface IPackage {
  id: string;
  authors: string;
  totalDownloads: number;
  version: string;
  tags: string[];
  description: string;
  iconUrl: string;
}

interface ISearchResultsState {
  includePrerelease: boolean;
  packageType: string;
  targetFramework: string;
  items: IPackage[];
}

interface ISearchResponse {
  data: IPackage[];
}

class SearchResults extends React.Component<ISearchResultsProps, ISearchResultsState> {

  private readonly defaultIconUrl: string = 'https://www.nuget.org/Content/gallery/img/default-package-icon-256x256.png';
  private resultsController?: AbortController;

  constructor(props: ISearchResultsProps) {
    super(props);

    this.state = {
      includePrerelease: true,
      items: [],
      packageType: 'any',
      targetFramework: 'any'
    };
  }

  public componentDidMount() {
    this._loadItems(
      this.props.input,
      this.state.includePrerelease,
      this.state.packageType,
      this.state.targetFramework);
  }

  public componentWillUnmount() {
    if (this.resultsController) {
      this.resultsController.abort();
    }
  }

  public componentWillReceiveProps(props: Readonly<ISearchResultsProps>) {
    if (props.input === this.props.input) {
      return;
    }

    this._loadItems(
      props.input,
      this.state.includePrerelease,
      this.state.packageType,
      this.state.targetFramework);
  }

  public render() {
    return (
      <div>
        <form className="search-options form-inline">
          <div className="form-group">
            <label>Package Type:</label>
            <select value={this.state.packageType} onChange={this.onChangePackageType} className="form-control">
              <option value="any">Any</option>
              <option value="dependency">Dependency</option>
              <option value="dotnettool">.NET Tool</option>
            </select>
          </div>

          <div className="form-group">
            <label>Framework:</label>
            <select value={this.state.targetFramework} onChange={this.onChangeFramework} className="form-control">
            <option value="any">Any</option>
              <option value="netstandard2.0">netstandard2.0</option>
              <option value="netstandard1.6">netstandard1.6</option>
              <option value="netstandard1.5">netstandard1.5</option>
              <option value="netstandard1.4">netstandard1.4</option>
              <option value="netstandard1.3">netstandard1.3</option>
              <option value="netstandard1.2">netstandard1.2</option>
              <option value="netstandard1.1">netstandard1.1</option>
              <option value="netstandard1.0">netstandard1.0</option>

              <option value="netcoreapp3.0">netcoreapp3.0</option>
              <option value="netcoreapp2.2">netcoreapp2.2</option>
              <option value="netcoreapp2.1">netcoreapp2.1</option>
              <option value="netcoreapp1.1">netcoreapp1.1</option>
              <option value="netcoreapp1.0">netcoreapp1.0</option>

              <option value="net472">net472</option>
              <option value="net471">net471</option>
              <option value="net463">net463</option>
              <option value="net462">net462</option>
              <option value="net461">net461</option>
              <option value="net46">net46</option>
              <option value="net452">net452</option>
              <option value="net451">net451</option>
              <option value="net45">net45</option>
              <option value="net403">net403</option>
              <option value="net4">net4</option>
              <option value="net35">net35</option>
              <option value="net2">net2</option>
              <option value="net11">net11</option>
            </select>
          </div>
          <div className="form-group checkbox">
            <label>
              Include prerelease:
              <input defaultChecked={this.state.includePrerelease} onChange={this.onChangePrerelease} type="checkbox" id="prerelease" />
            </label>
          </div>
        </form>
        {this.state.items.map(value => (
          <div key={value.id} className="row search-result">
            <div className="col-sm-1 hidden-xs hidden-sm">
              <img src={value.iconUrl || this.defaultIconUrl} className="package-icon img-responsive" onError={this.loadDefaultIcon} />
            </div>
            <div className="col-sm-11">
              <div>
                <Link to={`/packages/${value.id}`} className="package-title">{value.id}</Link>
                <span>by: {value.authors}</span>
              </div>
              <ul className="info">
                <li>
                  <span>
                    <Icon iconName="Download" className="ms-Icon" />
                    {value.totalDownloads.toLocaleString()} total downloads
                  </span>
                </li>
                <li>
                  <span>
                    <Icon iconName="Flag" className="ms-Icon" />
                    Latest version: {value.version}
                  </span>
                </li>
                <li>
                  <span>
                    <Icon iconName="Tag" className="ms-Icon" />
                    {value.tags.join(' ')}
                  </span>
                </li>
              </ul>
              <div>
                {value.description}
              </div>
            </div>
          </div>
        ))}
      </div>
    );
  }

  private _loadItems(query: string, includePrerelease: boolean, packageType: string, targetFramework: string): void {
    if (this.resultsController) {
      this.resultsController.abort();
    }

    this.resultsController = new AbortController();

    this.setState({
      includePrerelease,
      items: [],
      packageType,
      targetFramework,
    });

    // tslint:disable-next-line:no-console
    console.log(targetFramework);

    const url = this.buildUrl(query, includePrerelease, packageType, targetFramework);

    fetch(url, {signal: this.resultsController.signal}).then(response => {
      return response.json();
    }).then(resultsJson => {
      const results = resultsJson as ISearchResponse;

      this.setState({
        includePrerelease,
        items: results.data,
        targetFramework,
      });
    });
  }

  private buildUrl(query: string, includePrerelease: boolean, packageType?: string, targetFramework?: string) {
    const parameters: { [parameter: string]: string } = {
      semVerLevel: "2.0.0"
    };

    if (query && query.length !== 0) {
      parameters.q = query;
    }

    if (includePrerelease) {
      parameters.prerelease = 'true';
    }

    if (packageType && packageType !== 'any') {
      parameters.packageType = packageType;
    }

    if (targetFramework && targetFramework !== 'any') {
      parameters.framework = targetFramework;
    }

    const queryString = Object.keys(parameters)
      .map(k => `${k}=${encodeURIComponent(parameters[k])}`)
      .join('&');

    return `/v3/search?${queryString}`;
  }

  private loadDefaultIcon = (e: React.SyntheticEvent<HTMLImageElement>) => {
    e.currentTarget.src = this.defaultIconUrl;
  }

  private onChangePackageType = (e: React.ChangeEvent<HTMLSelectElement>) : void => {
    this._loadItems(
      this.props.input,
      this.state.includePrerelease,
      e.currentTarget.value,
      this.state.targetFramework);
  }

  private onChangeFramework = (e: React.ChangeEvent<HTMLSelectElement>) : void => {
    this._loadItems(
      this.props.input,
      this.state.includePrerelease,
      this.state.packageType,
      e.currentTarget.value);
  }

  private onChangePrerelease = () : void => {
    this._loadItems(
      this.props.input,
      !this.state.includePrerelease,
      this.state.packageType,
      this.state.targetFramework);
  }
}

export default SearchResults;
