import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import './SearchResults.css';

interface ISearchResultsProps {
  input: string;
  onSelect(value: string): void;
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
  items: IPackage[];
}

interface ISearchResponse {
  data: IPackage[];
}

class SearchResults extends React.Component<ISearchResultsProps, ISearchResultsState> {

  private readonly defaultIconUrl: string = 'https://www.nuget.org/Content/gallery/img/default-package-icon-256x256.png';

  constructor(props: ISearchResultsProps) {
    super(props);

    this.state = {items: []};
  }

  public componentDidMount() {
    this._loadItems(this.props.input);
  }

  public componentWillReceiveProps(props: Readonly<ISearchResultsProps>) {
    this._loadItems(props.input);
  }

  public render() {
    return (
      <div>
        {this.state.items.map(value => (
          <div key={value.id} className="row search-result">
            <div className="col-sm-1 hidden-xs hidden-sm">
              <img src={value.iconUrl || this.defaultIconUrl} className="package-icon img-responsive" onError={this.loadDefaultIcon} />
            </div>
            <div className="col-sm-11">
              <div>
                <a className="package-title" href="#" onClick={this.onSelect}>{value.id}</a>
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

  private _loadItems(query: string): void {
    const url = (query && query.length !== 0)
      ? `/v3/search?q=${encodeURIComponent(query)}`
      : `/v3/search`;

    fetch(url).then(response => {
      return response.json();
    }).then(resultsJson => {
      const results = resultsJson as ISearchResponse;

      this.setState({ items: results.data });
    });
  }

  private onSelect = (e: React.MouseEvent<HTMLAnchorElement>) => this.props.onSelect(e.currentTarget.text);

  private loadDefaultIcon = (e: React.SyntheticEvent<HTMLImageElement>) => e.currentTarget.src = this.defaultIconUrl;
}

export default SearchResults;