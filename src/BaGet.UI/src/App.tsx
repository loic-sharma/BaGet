import * as React from 'react';
import DisplayPackage from './DisplayPackage';
import SearchResults from './SearchResults';

interface IAppState {
  input: string;
  selected?: string;
}

class App extends React.Component<{}, IAppState> {

  constructor(props: {}) {
    super(props);

    this.state = { input: "" };
  }

  public render() {
    if (this.state.selected) {
      return (
        <div>
          {this._renderSearch()}

          <section role="main" className="container main-container">
            <DisplayPackage id={this.state.selected} />
          </section>
        </div>
      );
    } else {
      return (
        <div>
          {this._renderSearch()}

          <section role="main" className="container main-container">
            <SearchResults input={this.state.input} onSelect={this.handleSelect} />
          </section>
        </div>
      );
    }
  }

  private _renderSearch() {
    return (
      <nav>
        <div className="container">
          <div className="row">
            <div className="col-sm-12">
              <div id="logo">
                <h1><a href="/">BaGet</a></h1>
              </div>
            </div>
          </div>
        </div>
        <div className="container search-container">
          <div className="row">
            <input
              type="text"
              className="form-control"
              autoComplete="off"
              placeholder="Search packages"
              onChange={this.handleChange} />
          </div>
        </div>
      </nav>
    );
  }

  private handleChange = (input: React.ChangeEvent<HTMLInputElement>) =>
    this.setState({ input: input.target.value, selected: undefined });

  private handleSelect = (selected: string) =>
    this.setState({input: "", selected});
}

export default App;
