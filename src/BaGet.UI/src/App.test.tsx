// TODO: remove ' --env=jest-environment-jsdom-sixteen' from the npm test script when react-sctips get updated with newer jest version
// https://github.com/jefflau/jest-fetch-mock/issues/82


import * as React from 'react';
import * as ReactDOM from 'react-dom';
import { StaticRouter as Router } from 'react-router-dom';
import { render, waitFor, fireEvent } from '@testing-library/react'

import App from './App';

describe('Tests for App component', () => {
  it('Should render without crashing', () => {
    const div = document.createElement('div');
    ReactDOM.render(<Router><App /></Router>, div);
    ReactDOM.unmountComponentAtNode(div);
  });

  it('Should change inputs value when changes get triggered', async () => {
    const { getByTestId } = render(<Router><App /></Router>);
    const inputField = await waitFor<HTMLInputElement>(
      () => getByTestId('input-field') as HTMLInputElement
    );
    const packageName = 'BaGet.Core';

    fireEvent.change(
      inputField, 
      { target: { value: packageName}}
    );

    expect(inputField.value).toEqual(packageName);
  });
});