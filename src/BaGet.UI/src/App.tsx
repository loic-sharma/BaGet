import * as React from 'react';
import './App.css';
import DisplayPackage from './DisplayPackage/DisplayPackage';
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
      <nav className="navbar navbar-inverse" role="navigation">
        <div className="container">
          <div className="row">
            <div className="col-sm-12">
              <div id="navbar-header">
                <button type="button" className="navbar-toggle collapsed" data-toggle="collapse" data-target="#navbar">
                  <span className="icon-bar" />
                  <span className="icon-bar" />
                  <span className="icon-bar" />
                </button>

              </div>
            </div>
            <div id="navbar" className="navbar-collapse collapse">
              <ul className="nav navbar-nav" role="tablist">
                <li className="active" role="presentation"><a role="tab" href="/"><span>Packages</span></a></li>
                <li role="presentation"><a role="tab" href="/"><span>Upload</span></a></li>
                <li role="presentation"><a role="tab" href="https://loic-sharma.github.io/BaGet/"><span>Documentation</span></a></li>
              </ul>
            </div>
          </div>
        </div>
        <div className="container search-container">
          <div className="row">
            <form className="col-sm-12">
              <input
                type="text"
                className="form-control"
                autoComplete="off"
                placeholder="Search packages..."
              onChange={this.handleChange} />
            </form>
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
