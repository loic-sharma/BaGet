import * as React from "react";
import DisplayPackage from './DisplayPackage';
import SearchResults from './SearchResults';

interface AppState {
  input: string;
  selected?: string;
}

export default class App extends React.Component<{}, AppState> {

  constructor(props: {}) {
    super(props);

    this.state = {input: ""};
  }

  render() {
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
            <SearchResults input={this.state.input} onSelect={selected => this._handleSelect(selected)} />
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
              onChange={e => this._handleChange(e.target.value)} />
          </div>
        </div>
      </nav>
    );
  }

  private _handleChange(input: string) {
    this.setState({input: input, selected: undefined});
  }

  private _handleSelect(selected: string) {
    this.setState({input: "", selected: selected});
  }
}