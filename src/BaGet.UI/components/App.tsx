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

          <DisplayPackage id={this.state.selected} />
        </div>
      );
    } else {
      return (
        <div>
          {this._renderSearch()}

          <SearchResults input={this.state.input} onSelect={selected => this._handleSelect(selected)} />
        </div>
      );
    }
  }

  private _renderSearch() {
    return (
      <div>
        <input
          type="text"
          placeholder="Search"
          onChange={e => this._handleChange(e.target.value)} />
      </div>
    );
  }

  private _handleChange(input: string) {
    this.setState({input: input, selected: undefined});
  }

  private _handleSelect(selected: string) {
    this.setState({input: "", selected: selected});
  }
}