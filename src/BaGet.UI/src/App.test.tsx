import * as React from 'react';
import * as ReactDOM from 'react-dom';
import App from './App';
import { StaticRouter as Router } from 'react-router-dom';

it('renders without crashing', () => {
  const div = document.createElement('div');
  ReactDOM.render(<Router><App /></Router>, div);
  ReactDOM.unmountComponentAtNode(div);
});
